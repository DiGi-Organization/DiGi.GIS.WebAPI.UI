/*
 * DiGi.Harness chat widget (framework-free).
 *
 * Edited only in DiGi.Harness.WebAPI; hosts copy it into their wwwroot with a build target (as gltf-viewer-core.js),
 * and style it with their own design system through the harness-chat-* class names.
 *
 * Talks to HarnessChatController (harness/harnesschat):
 *   GET  configuration                 -> { Enabled, DefaultProviderName, Providers[], ThinkingVisibilities[] }
 *   POST sessions { Provider }         -> { Id, ProviderName, DisplayName }
 *   GET  sessions/{id}?thinkingvisibility=  -> the transcript (a DiGi Conversation)
 *   DELETE sessions/{id}
 *   GET  sessions/{id}/export?mode=Messages|Full -> a DiGi ConversationExport, as a file download (Content-Disposition)
 *   POST sessions/{id}/messages { Text, ThinkingVisibility } -> text/event-stream, one JSON event per data line
 *
 * Model text is never parsed as HTML: everything is built with createElement and textContent.
 */
(function (global) {
    'use strict';

    const VISIBILITY_LABELS = { Full: 'Full', Summary: 'Summary', None: 'None' };

    function typeName(jsonObject) {
        const value = jsonObject && jsonObject._type;
        if (typeof value !== 'string') {
            return '';
        }

        const fullName = value.split(',')[0];
        return fullName.substring(fullName.lastIndexOf('.') + 1);
    }

    function element(tagName, className, text) {
        const result = document.createElement(tagName);
        if (className) {
            result.className = className;
        }

        if (text !== undefined && text !== null) {
            result.textContent = text;
        }

        return result;
    }

    function formatSeconds(milliseconds) {
        if (!(milliseconds >= 0)) {
            return '';
        }

        return milliseconds < 10000 ? `${(milliseconds / 1000).toFixed(1)} s` : `${Math.round(milliseconds / 1000)} s`;
    }

    function formatTokens(count) {
        if (!(count > 0)) {
            return '0';
        }

        return count >= 1000 ? `${(count / 1000).toFixed(1)}k` : String(count);
    }

    function readStorage(storage, key) {
        try {
            return storage.getItem(key);
        } catch (error) {
            return null;
        }
    }

    function writeStorage(storage, key, value) {
        try {
            if (value === null || value === undefined) {
                storage.removeItem(key);
            } else {
                storage.setItem(key, value);
            }
        } catch (error) {
            // Storage can be unavailable (private mode, blocked site data); the chat works without it.
        }
    }

    // --- Minimal, safe markdown: paragraphs, headings, lists, code, bold, italic, inline code, tables. ---

    function appendInline(parent, text) {
        const pattern = /(`[^`]+`|\*\*[^*]+\*\*|\*[^*\s][^*]*\*)/g;
        let index = 0;
        let match;
        while ((match = pattern.exec(text)) !== null) {
            if (match.index > index) {
                parent.appendChild(document.createTextNode(text.substring(index, match.index)));
            }

            const token = match[0];
            if (token.startsWith('`')) {
                parent.appendChild(element('code', null, token.substring(1, token.length - 1)));
            } else if (token.startsWith('**')) {
                parent.appendChild(element('strong', null, token.substring(2, token.length - 2)));
            } else {
                parent.appendChild(element('em', null, token.substring(1, token.length - 1)));
            }

            index = match.index + token.length;
        }

        if (index < text.length) {
            parent.appendChild(document.createTextNode(text.substring(index)));
        }
    }

    function isTableSeparator(line) {
        return /^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/.test(line);
    }

    function tableCells(line) {
        let trimmed = line.trim();
        if (trimmed.startsWith('|')) {
            trimmed = trimmed.substring(1);
        }

        if (trimmed.endsWith('|')) {
            trimmed = trimmed.substring(0, trimmed.length - 1);
        }

        return trimmed.split('|').map(x => x.trim());
    }

    function renderMarkdown(container, text) {
        container.textContent = '';
        const lines = (text || '').replace(/\r\n/g, '\n').split('\n');
        let index = 0;
        let paragraph = [];

        function flushParagraph() {
            if (paragraph.length === 0) {
                return;
            }

            const p = element('p');
            paragraph.forEach((line, lineIndex) => {
                if (lineIndex > 0) {
                    p.appendChild(document.createElement('br'));
                }

                appendInline(p, line);
            });
            container.appendChild(p);
            paragraph = [];
        }

        while (index < lines.length) {
            const line = lines[index];

            if (line.trim().startsWith('```')) {
                flushParagraph();
                const code = [];
                index++;
                while (index < lines.length && !lines[index].trim().startsWith('```')) {
                    code.push(lines[index]);
                    index++;
                }

                index++;
                const pre = element('pre');
                pre.appendChild(element('code', null, code.join('\n')));
                container.appendChild(pre);
                continue;
            }

            const heading = /^(#{1,4})\s+(.*)$/.exec(line);
            if (heading) {
                flushParagraph();
                const h = element(`h${Math.min(heading[1].length + 2, 6)}`);
                appendInline(h, heading[2]);
                container.appendChild(h);
                index++;
                continue;
            }

            if (line.includes('|') && index + 1 < lines.length && isTableSeparator(lines[index + 1])) {
                flushParagraph();
                const wrapper = element('div', 'harness-chat-table');
                const table = element('table');
                const thead = element('thead');
                const headerRow = element('tr');
                tableCells(line).forEach(cell => {
                    const th = element('th');
                    appendInline(th, cell);
                    headerRow.appendChild(th);
                });
                thead.appendChild(headerRow);
                table.appendChild(thead);
                const tbody = element('tbody');
                index += 2;
                while (index < lines.length && lines[index].includes('|') && lines[index].trim() !== '') {
                    const row = element('tr');
                    tableCells(lines[index]).forEach(cell => {
                        const td = element('td');
                        appendInline(td, cell);
                        row.appendChild(td);
                    });
                    tbody.appendChild(row);
                    index++;
                }

                table.appendChild(tbody);
                wrapper.appendChild(table);
                container.appendChild(wrapper);
                continue;
            }

            const bullet = /^\s*[-*•]\s+(.*)$/.exec(line);
            const numbered = /^\s*\d+[.)]\s+(.*)$/.exec(line);
            if (bullet || numbered) {
                flushParagraph();
                const list = element(bullet ? 'ul' : 'ol');
                while (index < lines.length) {
                    const item = bullet ? /^\s*[-*•]\s+(.*)$/.exec(lines[index]) : /^\s*\d+[.)]\s+(.*)$/.exec(lines[index]);
                    if (!item) {
                        break;
                    }

                    const li = element('li');
                    appendInline(li, item[1]);
                    list.appendChild(li);
                    index++;
                }

                container.appendChild(list);
                continue;
            }

            if (line.trim() === '') {
                flushParagraph();
            } else {
                paragraph.push(line);
            }

            index++;
        }

        flushParagraph();
    }

    // --- Server-sent events over fetch (EventSource cannot POST). ---

    async function readEvents(response, onEvent) {
        const reader = response.body.getReader();
        const decoder = new TextDecoder('utf-8');
        let buffer = '';

        function dispatch(block) {
            const data = block
                .split('\n')
                .filter(line => line.startsWith('data:'))
                .map(line => line.substring(5).replace(/^ /, ''))
                .join('\n');

            if (data.length === 0) {
                return;
            }

            try {
                onEvent(JSON.parse(data));
            } catch (error) {
                console.warn('Harness chat: an event could not be read.', error);
            }
        }

        for (;;) {
            const { done, value } = await reader.read();
            if (done) {
                break;
            }

            buffer += decoder.decode(value, { stream: true }).replace(/\r\n/g, '\n');
            let separator;
            while ((separator = buffer.indexOf('\n\n')) >= 0) {
                dispatch(buffer.substring(0, separator));
                buffer = buffer.substring(separator + 2);
            }
        }

        buffer += decoder.decode();
        if (buffer.trim().length > 0) {
            dispatch(buffer);
        }
    }

    // --- The widget. ---

    function create(container, options) {
        const settings = Object.assign({
            baseUrl: '/harness/harnesschat',
            fetch: (url, init) => global.fetch(url, init),
            storageKey: 'digi.harness',
            title: 'Assistant',
            placeholder: 'Ask a question…',
            emptyText: 'Ask about the data. The assistant answers by calling the API and shows each call it makes.',
        }, options || {});

        const state = {
            configuration: null,
            sessionId: readStorage(global.sessionStorage, `${settings.storageKey}.session`),
            providerName: readStorage(global.localStorage, `${settings.storageKey}.provider`),
            thinkingVisibility: readStorage(global.localStorage, `${settings.storageKey}.thinking`) || 'Summary',
            abortController: null,
            running: false,
            exporting: false,
        };

        if (!VISIBILITY_LABELS[state.thinkingVisibility]) {
            state.thinkingVisibility = 'Summary';
        }

        // Layout.
        container.classList.add('harness-chat');
        container.textContent = '';

        const header = element('div', 'harness-chat-header');
        const title = element('div', 'harness-chat-title', settings.title);
        const controls = element('div', 'harness-chat-controls');

        const providerLabel = element('label', 'harness-chat-field');
        providerLabel.appendChild(element('span', 'harness-chat-field-label', 'Model'));
        const providerSelect = element('select', 'harness-chat-select');
        providerSelect.setAttribute('aria-label', 'Model');
        providerLabel.appendChild(providerSelect);

        const visibilityLabel = element('label', 'harness-chat-field');
        visibilityLabel.appendChild(element('span', 'harness-chat-field-label', 'Thinking'));
        const visibilitySelect = element('select', 'harness-chat-select');
        visibilitySelect.setAttribute('aria-label', 'Thinking visibility');
        Object.keys(VISIBILITY_LABELS).forEach(key => {
            const option = element('option', null, VISIBILITY_LABELS[key]);
            option.value = key;
            visibilitySelect.appendChild(option);
        });
        visibilitySelect.value = state.thinkingVisibility;
        visibilityLabel.appendChild(visibilitySelect);

        const newButton = element('button', 'harness-chat-new', 'New chat');
        newButton.type = 'button';

        const exportButton = element('button', 'harness-chat-export', 'Export');
        exportButton.type = 'button';
        exportButton.title = 'Export this chat to a JSON file';
        exportButton.disabled = true;

        controls.appendChild(providerLabel);
        controls.appendChild(visibilityLabel);
        controls.appendChild(newButton);
        controls.appendChild(exportButton);
        header.appendChild(title);
        header.appendChild(controls);

        const log = element('div', 'harness-chat-log');
        log.setAttribute('role', 'log');
        log.setAttribute('aria-live', 'polite');

        const status = element('div', 'harness-chat-status');
        status.setAttribute('aria-live', 'polite');

        const form = element('form', 'harness-chat-composer');
        const input = element('textarea', 'harness-chat-input');
        input.rows = 2;
        input.placeholder = settings.placeholder;
        input.setAttribute('aria-label', 'Message');
        input.maxLength = 8000;
        const sendButton = element('button', 'harness-chat-send', 'Send');
        sendButton.type = 'submit';
        form.appendChild(input);
        form.appendChild(sendButton);

        // Export dialog: the mode is asked every time and always starts on 'Messages only'.
        const exportDialog = element('dialog', 'harness-chat-export-dialog');
        exportDialog.setAttribute('aria-label', 'Export chat');
        const exportForm = element('form', 'harness-chat-export-form');
        exportForm.appendChild(element('div', 'harness-chat-export-title', 'Export chat'));
        const exportOptions = element('fieldset', 'harness-chat-export-options');
        exportOptions.appendChild(element('legend', 'harness-chat-export-legend', 'Include'));
        const exportRadios = [
            { value: 'Messages', label: 'Messages only', hint: 'Your questions and the assistant’s answers.' },
            { value: 'Full', label: 'Full', hint: 'Also the complete thinking, tool calls and tool results — for sharing with an AI model to fix bugs or optimise the assistant.' },
        ].map(option => {
            const optionLabel = element('label', 'harness-chat-export-option');
            const radio = element('input', 'harness-chat-export-radio');
            radio.type = 'radio';
            radio.name = 'harness-chat-export-mode';
            radio.value = option.value;
            const optionText = element('span', 'harness-chat-export-option-text');
            optionText.appendChild(element('span', 'harness-chat-export-option-label', option.label));
            optionText.appendChild(element('span', 'harness-chat-export-option-hint', option.hint));
            optionLabel.appendChild(radio);
            optionLabel.appendChild(optionText);
            exportOptions.appendChild(optionLabel);
            return radio;
        });
        exportForm.appendChild(exportOptions);
        const exportError = element('div', 'harness-chat-export-error');
        exportError.setAttribute('role', 'alert');
        exportError.hidden = true;
        exportForm.appendChild(exportError);
        const exportActions = element('div', 'harness-chat-export-actions');
        const exportCancelButton = element('button', 'harness-chat-export-cancel', 'Cancel');
        exportCancelButton.type = 'button';
        const exportConfirmButton = element('button', 'harness-chat-export-confirm', 'Export');
        exportConfirmButton.type = 'submit';
        exportActions.appendChild(exportCancelButton);
        exportActions.appendChild(exportConfirmButton);
        exportForm.appendChild(exportActions);
        exportDialog.appendChild(exportForm);

        container.appendChild(header);
        container.appendChild(log);
        container.appendChild(status);
        container.appendChild(form);
        container.appendChild(exportDialog);

        function setStatus(text, isError) {
            status.textContent = text || '';
            status.classList.toggle('harness-chat-status-error', !!isError);
        }

        function scrollToEnd() {
            log.scrollTop = log.scrollHeight;
        }

        function showEmpty() {
            if (log.childElementCount === 0) {
                log.appendChild(element('div', 'harness-chat-empty', settings.emptyText));
            }
        }

        function clearLog() {
            log.textContent = '';
            showEmpty();
        }

        function setRunning(running) {
            state.running = running;
            sendButton.textContent = running ? 'Stop' : 'Send';
            sendButton.classList.toggle('harness-chat-stop', running);
            input.disabled = running;
            providerSelect.disabled = running;
            visibilitySelect.disabled = running;
            newButton.disabled = running;
            container.classList.toggle('harness-chat-running', running);
            updateExportButton();
        }

        function updateExportButton() {
            exportButton.disabled = !state.sessionId || state.running || state.exporting;
        }

        function appendUserMessage(text) {
            const empty = log.querySelector('.harness-chat-empty');
            if (empty) {
                empty.remove();
            }

            const message = element('div', 'harness-chat-message harness-chat-message-user');
            message.appendChild(element('div', 'harness-chat-bubble', text));
            log.appendChild(message);
            scrollToEnd();
        }

        // One assistant turn: thinking, tool calls, answer, and a footer with the metrics.
        function createTurnView() {
            const empty = log.querySelector('.harness-chat-empty');
            if (empty) {
                empty.remove();
            }

            const message = element('div', 'harness-chat-message harness-chat-message-assistant');
            const thinking = element('details', 'harness-chat-thinking');
            thinking.hidden = true;
            const thinkingSummary = element('summary', null, 'Thinking');
            const thinkingBody = element('div', 'harness-chat-thinking-body');
            thinking.appendChild(thinkingSummary);
            thinking.appendChild(thinkingBody);

            const tools = element('div', 'harness-chat-tools');
            tools.hidden = true;

            const answer = element('div', 'harness-chat-answer');
            const footer = element('div', 'harness-chat-footer');

            message.appendChild(thinking);
            message.appendChild(tools);
            message.appendChild(answer);
            message.appendChild(footer);
            log.appendChild(message);

            const view = {
                answerText: '',
                thinkingFullText: null,
                toolCalls: {},
                renderPending: false,
                step: 0,
                activeTool: null,
                addThinkingDelta(text) {
                    thinking.hidden = false;
                    if (view.thinkingFullText === null) {
                        view.thinkingFullText = element('div', 'harness-chat-thinking-text');
                        thinkingBody.appendChild(view.thinkingFullText);
                    }

                    view.thinkingFullText.textContent += text;
                },
                addThinkingLine(text) {
                    thinking.hidden = false;
                    view.thinkingFullText = null;
                    thinkingBody.appendChild(element('div', 'harness-chat-thinking-line', text));
                },
                endThinkingBlock() {
                    view.thinkingFullText = null;
                },
                addText(text) {
                    view.answerText += text;
                    if (!view.renderPending) {
                        view.renderPending = true;
                        global.requestAnimationFrame(() => {
                            view.renderPending = false;
                            renderMarkdown(answer, view.answerText);
                            scrollToEnd();
                        });
                    }
                },
                startTool(id, name, inputJson) {
                    tools.hidden = false;
                    const item = element('details', 'harness-chat-tool harness-chat-tool-running');
                    const summary = element('summary');
                    let label = name || 'tool';
                    let parsed = null;
                    try {
                        parsed = inputJson ? JSON.parse(inputJson) : null;
                    } catch (error) {
                        parsed = null;
                    }

                    if (parsed && parsed.path) {
                        label = `${parsed.method || ''} ${parsed.path}`.trim();
                    }

                    summary.appendChild(element('span', 'harness-chat-tool-name', name || 'tool'));
                    summary.appendChild(element('span', 'harness-chat-tool-label', label));
                    const metrics = element('span', 'harness-chat-tool-metrics', '…');
                    summary.appendChild(metrics);
                    item.appendChild(summary);
                    item.appendChild(element('pre', 'harness-chat-tool-input', parsed ? JSON.stringify(parsed, null, 2) : (inputJson || '')));
                    tools.appendChild(item);
                    view.toolCalls[id] = { item, metrics };
                    view.activeTool = label;
                },
                endTool(id, isError, durationMilliseconds, characters, truncated) {
                    const toolCall = view.toolCalls[id];
                    if (!toolCall) {
                        return;
                    }

                    toolCall.item.classList.remove('harness-chat-tool-running');
                    toolCall.item.classList.add(isError ? 'harness-chat-tool-error' : 'harness-chat-tool-ok');
                    const parts = [isError ? 'failed' : 'ok', formatSeconds(durationMilliseconds)];
                    if (characters > 0) {
                        parts.push(`${formatTokens(characters)} chars${truncated ? ', truncated' : ''}`);
                    }

                    toolCall.metrics.textContent = parts.filter(Boolean).join(' · ');
                },
                showError(text) {
                    message.appendChild(element('div', 'harness-chat-error', text));
                },
                complete(turnResult, elapsedMilliseconds) {
                    renderMarkdown(answer, view.answerText);
                    if (!view.answerText && !answer.childElementCount) {
                        answer.hidden = true;
                    }

                    const parts = [];
                    if (turnResult) {
                        parts.push(`${turnResult.Steps || 0} step${turnResult.Steps === 1 ? '' : 's'}`);
                        parts.push(`${turnResult.ToolCalls || 0} tool call${turnResult.ToolCalls === 1 ? '' : 's'}`);
                        parts.push(formatSeconds(turnResult.TotalMilliseconds || elapsedMilliseconds));
                        const usage = turnResult.Usage;
                        if (usage) {
                            const input = (usage.InputTokens || 0) + (usage.CacheReadInputTokens || 0) + (usage.CacheCreationInputTokens || 0);
                            parts.push(`${formatTokens(input)} in / ${formatTokens(usage.OutputTokens || 0)} out tokens`);
                        }
                    } else if (elapsedMilliseconds) {
                        parts.push(formatSeconds(elapsedMilliseconds));
                    }

                    footer.textContent = parts.join(' · ');
                    scrollToEnd();
                },
            };

            return view;
        }

        function renderTranscript(conversation) {
            log.textContent = '';
            const messages = (conversation && conversation.Messages) || [];
            let view = null;
            const toolNames = {};

            messages.forEach(message => {
                const contents = message.Contents || [];
                const isUser = message.Role === 1 || message.Role === 'User';
                const toolResults = contents.filter(x => typeName(x) === 'ToolResultContent');

                if (isUser && toolResults.length === contents.length && view) {
                    toolResults.forEach(x => view.endTool(x.ToolUseId, !!x.IsError, -1, 0, false));
                    return;
                }

                if (isUser) {
                    const text = contents.filter(x => typeName(x) === 'TextContent').map(x => x.Text || '').join('\n\n');
                    if (text) {
                        if (view) {
                            view.complete(null, 0);
                        }

                        view = null;
                        appendUserMessage(text);
                    }

                    return;
                }

                if (!view) {
                    view = createTurnView();
                }

                contents.forEach(content => {
                    switch (typeName(content)) {
                        case 'ThinkingContent':
                            if (content.Text) {
                                if (state.thinkingVisibility === 'Full') {
                                    view.addThinkingDelta(content.Text);
                                    view.endThinkingBlock();
                                } else {
                                    view.addThinkingLine(content.Text);
                                }
                            }

                            break;
                        case 'TextContent':
                            view.answerText += (view.answerText ? '\n\n' : '') + (content.Text || '');
                            break;
                        case 'ToolUseContent':
                            toolNames[content.Id] = content.Name;
                            view.startTool(content.Id, content.Name, content.InputJson);
                            break;
                        default:
                            break;
                    }
                });
            });

            if (view) {
                view.complete(null, 0);
            }

            showEmpty();
            scrollToEnd();
        }

        async function request(path, init) {
            return settings.fetch(`${settings.baseUrl}${path}`, Object.assign({ credentials: 'same-origin' }, init || {}));
        }

        async function loadConfiguration() {
            let response;
            try {
                response = await request('/configuration', { method: 'GET', headers: { Accept: 'application/json' } });
            } catch (error) {
                return null;
            }

            if (!response.ok) {
                return null;
            }

            const configuration = await response.json();
            state.configuration = configuration;

            providerSelect.textContent = '';
            (configuration.Providers || []).forEach(provider => {
                const option = element('option', null, provider.DisplayName || provider.Name);
                option.value = provider.Name;
                option.title = provider.Model || '';
                providerSelect.appendChild(option);
            });

            const names = (configuration.Providers || []).map(x => x.Name);
            if (!state.providerName || names.indexOf(state.providerName) < 0) {
                state.providerName = configuration.DefaultProviderName || names[0] || null;
            }

            if (state.providerName) {
                providerSelect.value = state.providerName;
            }

            providerLabel.hidden = names.length < 2;
            return configuration;
        }

        async function createSession() {
            const response = await request('/sessions', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
                body: JSON.stringify({ Provider: state.providerName }),
            });

            if (!response.ok) {
                throw new Error(statusText(response.status));
            }

            const session = await response.json();
            state.sessionId = session.Id;
            writeStorage(global.sessionStorage, `${settings.storageKey}.session`, state.sessionId);
            updateExportButton();
            return session;
        }

        async function restoreSession() {
            if (!state.sessionId) {
                clearLog();
                return;
            }

            let response;
            try {
                response = await request(`/sessions/${encodeURIComponent(state.sessionId)}?thinkingvisibility=${encodeURIComponent(state.thinkingVisibility)}`, { method: 'GET', headers: { Accept: 'application/json' } });
            } catch (error) {
                clearLog();
                return;
            }

            if (!response.ok) {
                state.sessionId = null;
                writeStorage(global.sessionStorage, `${settings.storageKey}.session`, null);
                updateExportButton();
                clearLog();
                return;
            }

            const data = await response.json();
            const conversation = Array.isArray(data) ? data[0] : data;
            if (conversation && conversation.ProviderName && state.configuration && (state.configuration.Providers || []).some(x => x.Name === conversation.ProviderName)) {
                state.providerName = conversation.ProviderName;
                providerSelect.value = state.providerName;
            }

            renderTranscript(conversation);
        }

        function statusText(statusCode) {
            switch (statusCode) {
                case 400: return 'The message could not be sent (empty or too long).';
                case 401: return 'Your session has ended. Sign in again to use the assistant.';
                case 404: return 'The chat has expired.';
                case 409: return 'The assistant is still answering the previous message.';
                case 503: return 'The assistant is not available right now.';
                default: return `The assistant could not be reached (HTTP ${statusCode}).`;
            }
        }

        async function startNewChat() {
            const sessionId = state.sessionId;
            state.sessionId = null;
            writeStorage(global.sessionStorage, `${settings.storageKey}.session`, null);
            updateExportButton();
            clearLog();
            setStatus('');
            if (sessionId) {
                try {
                    await request(`/sessions/${encodeURIComponent(sessionId)}`, { method: 'DELETE' });
                } catch (error) {
                    // The session expires on its own.
                }
            }
        }

        async function send(text) {
            setRunning(true);
            appendUserMessage(text);
            const view = createTurnView();
            const startedAt = global.performance.now();
            let lastElapsed = 0;
            let step = 0;
            let completed = false;

            const timer = global.setInterval(() => {
                const elapsed = global.performance.now() - startedAt;
                const parts = [`Working… ${formatSeconds(elapsed)}`];
                if (step > 0) {
                    parts.push(`step ${step}`);
                }

                if (view.activeTool) {
                    parts.push(view.activeTool);
                }

                setStatus(parts.join(' · '));
            }, 100);

            state.abortController = new AbortController();

            try {
                if (!state.sessionId) {
                    await createSession();
                }

                let response = await postMessage(text);
                if (response.status === 404) {
                    await createSession();
                    view.showError('The previous chat had expired; this message starts a new one.');
                    response = await postMessage(text);
                }

                if (!response.ok || !response.body) {
                    view.showError(statusText(response.status));
                    return;
                }

                await readEvents(response, event => {
                    lastElapsed = event.ElapsedMilliseconds || lastElapsed;
                    switch (typeName(event)) {
                        case 'StepStartedEvent':
                            step = event.Step;
                            view.activeTool = null;
                            break;
                        case 'TextDeltaEvent':
                            view.addText(event.Text || '');
                            break;
                        case 'ThinkingDeltaEvent':
                            view.addThinkingDelta(event.Text || '');
                            break;
                        case 'ThinkingSummaryEvent':
                            view.addThinkingLine(event.Text || '');
                            break;
                        case 'ToolCallStartedEvent':
                            view.endThinkingBlock();
                            view.startTool(event.ToolUseId, event.ToolName, event.InputJson);
                            break;
                        case 'ToolCallCompletedEvent':
                            view.endTool(event.ToolUseId, !!event.IsError, event.DurationMilliseconds, event.Characters, !!event.Truncated);
                            view.activeTool = null;
                            break;
                        case 'ErrorEvent':
                            view.showError(event.Message || 'The assistant failed.');
                            break;
                        case 'TurnCompletedEvent':
                            completed = true;
                            view.complete(event.TurnResult, lastElapsed);
                            break;
                        default:
                            break;
                    }

                    scrollToEnd();
                });

                if (!completed) {
                    view.showError('The answer stream ended early.');
                    view.complete(null, global.performance.now() - startedAt);
                }
            } catch (error) {
                if (error && error.name === 'AbortError') {
                    view.showError('Stopped.');
                } else {
                    view.showError(error && error.message ? error.message : 'The assistant could not be reached.');
                }

                view.complete(null, global.performance.now() - startedAt);
            } finally {
                global.clearInterval(timer);
                state.abortController = null;
                setRunning(false);
                setStatus('');
                input.focus();
            }
        }

        function fileName(contentDisposition) {
            const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(contentDisposition || '');
            return match ? decodeURIComponent(match[1]) : 'chat-export.json';
        }

        function exportStatusText(statusCode) {
            switch (statusCode) {
                case 404: return 'The chat has expired; start a new chat.';
                case 409: return 'The assistant is still answering; export once the reply has finished.';
                default: return statusText(statusCode);
            }
        }

        function openExportDialog() {
            if (!state.sessionId || state.running) {
                return;
            }

            exportRadios.forEach(radio => {
                radio.checked = radio.value === 'Messages';
            });
            exportError.hidden = true;
            exportError.textContent = '';
            exportConfirmButton.disabled = false;
            exportDialog.showModal();
            exportRadios[0].focus();
        }

        async function exportChat(mode) {
            state.exporting = true;
            updateExportButton();
            exportConfirmButton.disabled = true;
            exportError.hidden = true;

            try {
                const response = await request(`/sessions/${encodeURIComponent(state.sessionId)}/export?mode=${encodeURIComponent(mode)}`, { method: 'GET', headers: { Accept: 'application/json' } });
                if (!response.ok) {
                    exportError.textContent = exportStatusText(response.status);
                    exportError.hidden = false;
                    return;
                }

                const blob = await response.blob();
                const url = global.URL.createObjectURL(blob);
                const link = element('a');
                link.href = url;
                link.download = fileName(response.headers.get('Content-Disposition'));
                link.hidden = true;
                document.body.appendChild(link);
                link.click();
                link.remove();
                global.setTimeout(() => global.URL.revokeObjectURL(url), 0);
                exportDialog.close();
            } catch (error) {
                exportError.textContent = 'The chat could not be exported.';
                exportError.hidden = false;
            } finally {
                state.exporting = false;
                exportConfirmButton.disabled = false;
                updateExportButton();
            }
        }

        function postMessage(text) {
            return request(`/sessions/${encodeURIComponent(state.sessionId)}/messages`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
                body: JSON.stringify({ Text: text, ThinkingVisibility: state.thinkingVisibility }),
                signal: state.abortController ? state.abortController.signal : undefined,
            });
        }

        form.addEventListener('submit', event => {
            event.preventDefault();
            if (state.running) {
                if (state.abortController) {
                    state.abortController.abort();
                }

                return;
            }

            const text = input.value.trim();
            if (!text) {
                return;
            }

            input.value = '';
            send(text);
        });

        input.addEventListener('keydown', event => {
            if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
                event.preventDefault();
                form.requestSubmit();
            }
        });

        visibilitySelect.addEventListener('change', () => {
            state.thinkingVisibility = visibilitySelect.value;
            writeStorage(global.localStorage, `${settings.storageKey}.thinking`, state.thinkingVisibility);
            if (state.sessionId) {
                restoreSession();
            }
        });

        providerSelect.addEventListener('change', () => {
            state.providerName = providerSelect.value;
            writeStorage(global.localStorage, `${settings.storageKey}.provider`, state.providerName);
            startNewChat();
        });

        newButton.addEventListener('click', () => {
            startNewChat();
            input.focus();
        });

        exportButton.addEventListener('click', openExportDialog);

        exportCancelButton.addEventListener('click', () => {
            exportDialog.close();
        });

        exportForm.addEventListener('submit', event => {
            event.preventDefault();
            if (state.exporting || !state.sessionId) {
                return;
            }

            const checked = exportRadios.find(radio => radio.checked);
            exportChat(checked ? checked.value : 'Messages');
        });

        exportDialog.addEventListener('close', () => {
            if (!exportButton.disabled) {
                exportButton.focus();
            }
        });

        showEmpty();
        updateExportButton();

        return {
            element: container,
            loadConfiguration,
            restoreSession,
            focus() {
                input.focus();
            },
            isRunning() {
                return state.running;
            },
        };
    }

    global.DiGiHarnessChat = { create, renderMarkdown };
})(window);
