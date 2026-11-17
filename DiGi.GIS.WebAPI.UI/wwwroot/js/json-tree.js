// DiGi.GIS.WebAPI.UI — interactive JSON inspector for DiGi-serialized objects.
// Used by the Properties panel of the 3D viewers (gltf-viewer.js) and its pop-out window.
//
// Features: collapsible tree with lazily rendered children (arrays in chunks), DiGi "_type"
// members folded into short type badges (toggle to show them raw), inline previews of small
// objects (Point2D -> "X: 1.2, Y: 3.4") and of parameters ("Code = 1465"), search with match
// highlighting and next/previous navigation, expand/collapse all, raw (syntax-highlighted) JSON
// view, copy of the whole object, of a node's value or of its path, keyboard navigation
// (arrows, Home/End, Enter/Space) and a footer showing the selected node's path.
// Expanded paths survive setData, so selecting another object of the same type keeps the same
// branches open. Values are always written with textContent: the data is never parsed as HTML.

const TYPE_KEY = '_type';
const CHUNK_SIZE = 200;
const EXPAND_ALL_NODE_LIMIT = 20000;
const SEARCH_MATCH_LIMIT = 1000;
const PREFERENCES_KEY = 'digi.jsonTree.preferences';

const ICONS = {
    expandAll: '<path d="m7 6 5 5 5-5"/><path d="m7 13 5 5 5-5"/>',
    collapseAll: '<path d="m7 11 5-5 5 5"/><path d="m7 18 5-5 5 5"/>',
    types: '<path d="M12.6 2.6A2 2 0 0 0 11.2 2H4a2 2 0 0 0-2 2v7.2a2 2 0 0 0 .6 1.4l8.7 8.7a2.4 2.4 0 0 0 3.4 0l6.6-6.6a2.4 2.4 0 0 0 0-3.4z"/><circle cx="7.5" cy="7.5" r="1.5"/>',
    raw: '<path d="M8 3H7a2 2 0 0 0-2 2v5a2 2 0 0 1-2 2 2 2 0 0 1 2 2v5a2 2 0 0 0 2 2h1"/><path d="M16 21h1a2 2 0 0 0 2-2v-5a2 2 0 0 1 2-2 2 2 0 0 1-2-2V5a2 2 0 0 0-2-2h-1"/>',
    copy: '<rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>',
    check: '<path d="M20 6 9 17l-5-5"/>',
    popOut: '<path d="M15 3h6v6"/><path d="M10 14 21 3"/><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/>',
    up: '<path d="m18 15-6-6-6 6"/>',
    down: '<path d="m6 9 6 6 6-6"/>',
    caret: '<path d="m9 18 6-6-6-6"/>',
    path: '<path d="M4 7V4h16v3"/><path d="M9 20h6"/><path d="M12 4v16"/>'
};

function icon(name, size = 14) {
    const span = document.createElement('span');
    span.className = 'jt-icon';
    span.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${ICONS[name]}</svg>`;
    return span;
}

function readPreferences() {
    try {
        const parsed = JSON.parse(localStorage.getItem(PREFERENCES_KEY) || '{}');
        return parsed && typeof parsed === 'object' ? parsed : {};
    } catch {
        return {};
    }
}

function writePreferences(preferences) {
    try {
        localStorage.setItem(PREFERENCES_KEY, JSON.stringify(preferences));
    } catch {
        // Storage can be unavailable (private window, blocked site data): preferences stay in memory.
    }
}

/**
 * Short class name of a DiGi type string such as
 * "DiGi.Geometry.Planar.Classes.Point2D,DiGi.Geometry" -> "Point2D".
 * Generic arguments ("List`1[[...]]") are dropped.
 */
export function shortTypeName(type) {
    if (typeof type !== 'string' || type.length === 0) {
        return null;
    }
    const withoutGenerics = type.split('[')[0];
    const name = withoutGenerics.split(',')[0].split('.').pop();
    return name.replace(/`\d+$/, '') || null;
}

/** Copies text to the clipboard; falls back to a hidden textarea outside a secure context. */
export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
    } catch {
        const textarea = document.createElement('textarea');
        textarea.value = text;
        textarea.style.position = 'fixed';
        textarea.style.opacity = '0';
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand('copy');
        textarea.remove();
    }
}

function isContainer(value) {
    return value !== null && typeof value === 'object';
}

function childPath(path, key) {
    if (typeof key === 'number') {
        return `${path}[${key}]`;
    }
    return /^[A-Za-z_$][\w$]*$/.test(key) ? `${path}.${key}` : `${path}[${JSON.stringify(key)}]`;
}

function formatNumber(value) {
    if (!Number.isFinite(value) || Number.isInteger(value)) {
        return String(value);
    }
    // Trims binary floating point noise (7.180477003767727 stays, -7.665000000153668 -> -7.665000000154)
    // for display only; the exact value is in the tooltip and in every copy.
    return String(Number.parseFloat(value.toPrecision(13)));
}

function parameterName(value) {
    const definition = value?.ParameterDefinition;
    if (!isContainer(definition)) {
        return null;
    }
    if (typeof definition.Name === 'string' && definition.Name) {
        return definition.Name;
    }
    if (typeof definition.Enum === 'string' && definition.Enum.includes(':')) {
        return definition.Enum.split(':').pop();
    }
    return null;
}

export class JsonTree {
    /**
     * @param {object} [options]
     * @param {boolean} [options.popOut] Shows the pop-out button (opens the same data in a large window).
     * @param {string} [options.emptyMessage] Text shown while no data is set.
     */
    constructor(options = {}) {
        this.options = options;
        this.preferences = { showTypes: false, raw: false, ...readPreferences() };
        this.value = undefined;
        this.reference = null;
        this.expanded = new Set();
        this.chunkLimits = new Map();
        this.searchTerm = '';
        this.matchPaths = new Set();
        this.matchRows = [];
        this.matchIndex = -1;
        this.selectedPath = null;

        this.element = document.createElement('div');
        this.element.className = 'json-tree';
        this.buildToolbar();

        this.header = document.createElement('div');
        this.header.className = 'jt-header';
        this.element.appendChild(this.header);

        this.body = document.createElement('div');
        this.body.className = 'jt-body';
        this.body.setAttribute('role', 'tree');
        this.body.setAttribute('aria-label', 'Object properties');
        this.body.addEventListener('keydown', (event) => this.onKeyDown(event));
        this.element.appendChild(this.body);

        this.status = document.createElement('div');
        this.status.className = 'jt-status';
        this.element.appendChild(this.status);

        this.setEmpty(options.emptyMessage ?? 'No data.');
    }

    // --- Toolbar -------------------------------------------------------------------------------

    buildToolbar() {
        const toolbar = document.createElement('div');
        toolbar.className = 'jt-toolbar';

        const search = document.createElement('div');
        search.className = 'jt-search';
        this.searchInput = document.createElement('input');
        this.searchInput.type = 'search';
        this.searchInput.placeholder = 'Search keys and values';
        this.searchInput.setAttribute('aria-label', 'Search keys and values');
        this.searchInput.spellcheck = false;
        let debounce = 0;
        this.searchInput.addEventListener('input', () => {
            clearTimeout(debounce);
            debounce = setTimeout(() => this.setSearch(this.searchInput.value), 150);
        });
        this.searchInput.addEventListener('keydown', (event) => {
            if (event.key === 'Enter') {
                event.preventDefault();
                clearTimeout(debounce);
                if (this.searchInput.value.trim().toLowerCase() !== this.searchTerm) {
                    this.setSearch(this.searchInput.value);
                } else {
                    this.moveMatch(event.shiftKey ? -1 : 1);
                }
            } else if (event.key === 'Escape' && this.searchInput.value) {
                event.preventDefault();
                event.stopPropagation();
                this.searchInput.value = '';
                this.setSearch('');
            }
        });
        search.appendChild(this.searchInput);

        this.matchCounter = document.createElement('span');
        this.matchCounter.className = 'jt-match-counter';
        search.appendChild(this.matchCounter);
        this.previousMatchButton = this.toolButton('up', 'Previous match (Shift+Enter)', () => this.moveMatch(-1));
        this.nextMatchButton = this.toolButton('down', 'Next match (Enter)', () => this.moveMatch(1));
        search.append(this.previousMatchButton, this.nextMatchButton);
        toolbar.appendChild(search);

        const actions = document.createElement('div');
        actions.className = 'jt-actions-bar';
        this.expandAllButton = this.toolButton('expandAll', 'Expand all', () => this.expandAll());
        this.collapseAllButton = this.toolButton('collapseAll', 'Collapse all', () => this.collapseAll());
        this.typesButton = this.toolButton('types', 'Show the raw "_type" members', () => {
            this.preferences.showTypes = !this.preferences.showTypes;
            writePreferences(this.preferences);
            this.refresh();
        });
        this.rawButton = this.toolButton('raw', 'Raw JSON view', () => {
            this.preferences.raw = !this.preferences.raw;
            writePreferences(this.preferences);
            this.refresh();
        });
        this.copyButton = this.toolButton('copy', 'Copy the whole object as JSON', () => {
            this.copyWithFeedback(this.copyButton, JSON.stringify(this.value, null, 2));
        });
        actions.append(this.expandAllButton, this.collapseAllButton, this.typesButton, this.rawButton, this.copyButton);
        if (this.options.popOut) {
            this.popOutButton = this.toolButton('popOut', 'Open in a larger window', () => openJsonTreeWindow(this.value, this.reference, this));
            actions.appendChild(this.popOutButton);
        }
        toolbar.appendChild(actions);

        this.toolbar = toolbar;
        this.element.appendChild(toolbar);
    }

    toolButton(iconName, title, onClick) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'jt-tool';
        button.title = title;
        button.setAttribute('aria-label', title);
        button.appendChild(icon(iconName));
        button.addEventListener('click', onClick);
        return button;
    }

    async copyWithFeedback(button, text) {
        await copyText(text);
        const original = button.firstChild;
        button.replaceChildren(icon('check'));
        button.classList.add('jt-copied');
        setTimeout(() => {
            button.replaceChildren(original);
            button.classList.remove('jt-copied');
        }, 1100);
    }

    updateToolbarState() {
        const hasData = this.value !== undefined;
        this.typesButton.classList.toggle('jt-active', this.preferences.showTypes);
        this.typesButton.setAttribute('aria-pressed', String(this.preferences.showTypes));
        this.rawButton.classList.toggle('jt-active', this.preferences.raw);
        this.rawButton.setAttribute('aria-pressed', String(this.preferences.raw));
        for (const button of [this.expandAllButton, this.collapseAllButton, this.typesButton, this.rawButton, this.copyButton, this.popOutButton]) {
            if (button) {
                button.disabled = !hasData;
            }
        }
        this.expandAllButton.disabled ||= this.preferences.raw;
        this.collapseAllButton.disabled ||= this.preferences.raw;
        this.searchInput.disabled = !hasData;

        const searching = this.searchTerm.length > 0;
        this.matchCounter.textContent = !searching ? ''
            : this.matchRows.length === 0 ? 'No matches'
                : `${this.matchIndex + 1} / ${this.matchRows.length}${this.matchPaths.size >= SEARCH_MATCH_LIMIT ? '+' : ''}`;
        this.matchCounter.classList.toggle('jt-no-match', searching && this.matchRows.length === 0);
        this.previousMatchButton.disabled = this.matchRows.length < 2;
        this.nextMatchButton.disabled = this.matchRows.length < 2;
        this.previousMatchButton.hidden = !searching;
        this.nextMatchButton.hidden = !searching;
    }

    // --- Data ----------------------------------------------------------------------------------

    /**
     * Shows a message in place of the tree (toolbar disabled).
     * @param {string} message Text shown in the body.
     * @param {string|null} [reference] Reference still shown in the header, when known.
     */
    setEmpty(message, reference = null) {
        this.value = undefined;
        this.reference = reference;
        this.renderHeader();
        const span = document.createElement('div');
        span.className = 'jt-empty';
        span.textContent = message;
        this.body.replaceChildren(span);
        this.status.hidden = true;
        this.matchRows = [];
        this.updateToolbarState();
    }

    /**
     * Shows the value. Expanded branches and the search term are kept from the previous value.
     * @param {*} value Any JSON value.
     * @param {string|null} [reference] Reference shown in the header (copyable).
     */
    setData(value, reference = null) {
        this.value = value;
        this.reference = reference;
        this.selectedPath = null;
        this.chunkLimits.clear();
        this.renderHeader();
        this.refresh();
    }

    renderHeader() {
        this.header.replaceChildren();
        const type = isContainer(this.value) && !Array.isArray(this.value) ? this.value[TYPE_KEY] : null;
        this.header.hidden = !type && !this.reference;

        if (type) {
            const line = document.createElement('div');
            line.className = 'jt-header-type';
            const badge = document.createElement('span');
            badge.className = 'jt-type jt-type-root';
            badge.textContent = shortTypeName(type);
            const full = document.createElement('span');
            full.className = 'jt-header-namespace';
            full.textContent = type.split(',')[0];
            line.title = type;
            line.append(badge, full);
            this.header.appendChild(line);
        }

        if (this.reference) {
            const line = document.createElement('div');
            line.className = 'jt-header-reference';
            const label = document.createElement('span');
            label.className = 'jt-header-label';
            label.textContent = 'Reference';
            const text = document.createElement('span');
            text.className = 'jt-header-reference-text';
            text.textContent = this.reference;
            text.title = 'Click to show the whole reference';
            text.addEventListener('click', () => {
                const expanded = text.classList.toggle('jt-expanded');
                text.title = expanded ? 'Click to shorten' : 'Click to show the whole reference';
            });
            const copy = this.toolButton('copy', 'Copy reference', () => this.copyWithFeedback(copy, this.reference));
            copy.classList.add('jt-tool-inline');
            line.append(label, text, copy);
            this.header.appendChild(line);
        }
    }

    refresh() {
        if (this.value === undefined) {
            this.updateToolbarState();
            return;
        }
        this.element.classList.toggle('jt-raw-mode', this.preferences.raw);
        if (this.preferences.raw) {
            this.renderRaw();
        } else {
            this.computeMatches();
            this.renderTree();
        }
        this.updateStatus();
        this.updateToolbarState();
    }

    visibleEntries(value) {
        if (Array.isArray(value)) {
            return value.map((item, index) => [index, item]);
        }
        const entries = Object.entries(value);
        return this.preferences.showTypes ? entries : entries.filter(([key]) => key !== TYPE_KEY);
    }

    // --- Tree rendering ------------------------------------------------------------------------

    renderTree() {
        const scrollTop = this.body.scrollTop;
        this.matchRows = [];
        const list = document.createElement('ul');
        list.className = 'jt-list jt-root';
        list.setAttribute('role', 'group');

        if (isContainer(this.value)) {
            this.renderChildren(list, this.value, '$', 0);
        } else {
            list.appendChild(this.renderNode('value', this.value, '$', 0));
        }

        this.body.replaceChildren(list);
        this.body.scrollTop = scrollTop;

        const rows = this.body.querySelectorAll('.jt-row');
        const selected = this.selectedPath ? this.body.querySelector(`.jt-row[data-path="${CSS.escape(this.selectedPath)}"]`) : null;
        (selected ?? rows[0])?.setAttribute('tabindex', '0');

        if (this.searchTerm) {
            this.matchRows = Array.from(this.body.querySelectorAll('.jt-row.jt-match'));
            this.matchIndex = this.matchRows.length > 0 ? Math.min(Math.max(this.matchIndex, 0), this.matchRows.length - 1) : -1;
            this.highlightCurrentMatch(false);
        }
    }

    renderChildren(list, value, path, depth) {
        const entries = this.visibleEntries(value);
        const limit = this.chunkLimits.get(path) ?? CHUNK_SIZE;
        const shown = Math.min(entries.length, limit);
        for (let index = 0; index < shown; index++) {
            const [key, child] = entries[index];
            list.appendChild(this.renderNode(key, child, childPath(path, key), depth + 1));
        }

        if (entries.length === 0) {
            const empty = document.createElement('li');
            empty.className = 'jt-empty-container';
            empty.textContent = Array.isArray(value) ? 'empty array' : 'no members';
            list.appendChild(empty);
        }

        if (shown < entries.length) {
            list.appendChild(this.moreButton(list, entries, shown, path, depth));
        }
    }

    moreButton(list, entries, start, path, depth) {
        const more = document.createElement('li');
        more.className = 'jt-more';
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'jt-more-button';
        const remaining = entries.length - start;
        button.textContent = `Show ${Math.min(remaining, CHUNK_SIZE)} more (${remaining} remaining)`;
        button.addEventListener('click', () => {
            more.remove();
            const end = Math.min(entries.length, start + CHUNK_SIZE);
            this.chunkLimits.set(path, end);
            for (let index = start; index < end; index++) {
                const [key, child] = entries[index];
                list.appendChild(this.renderNode(key, child, childPath(path, key), depth + 1));
            }
            if (end < entries.length) {
                list.appendChild(this.moreButton(list, entries, end, path, depth));
            }
        });
        more.appendChild(button);
        return more;
    }

    renderNode(key, value, path, depth) {
        const item = document.createElement('li');
        item.className = 'jt-item';
        item.setAttribute('role', 'treeitem');

        const row = document.createElement('div');
        row.className = 'jt-row';
        row.dataset.path = path;
        row.tabIndex = -1;
        row.style.setProperty('--jt-depth', String(depth - 1));
        item.appendChild(row);

        const container = isContainer(value);
        const caret = document.createElement('span');
        caret.className = 'jt-caret';
        if (container) {
            caret.appendChild(icon('caret', 12));
        }
        row.appendChild(caret);

        const keyElement = document.createElement('span');
        keyElement.className = typeof key === 'number' ? 'jt-key jt-index' : key === TYPE_KEY ? 'jt-key jt-key-type' : 'jt-key';
        this.writeHighlighted(keyElement, String(key));
        row.appendChild(keyElement);

        if (container) {
            this.renderContainerSummary(row, value);
            const expanded = this.expanded.has(path);
            item.setAttribute('aria-expanded', String(expanded));
            if (expanded) {
                item.appendChild(this.childList(value, path, depth));
            }
            row.addEventListener('click', (event) => {
                if (event.target.closest('.jt-row-actions')) {
                    return;
                }
                this.select(row);
                this.toggle(item, value, path, depth);
            });
        } else {
            const valueElement = this.primitiveElement(value);
            row.appendChild(valueElement);
            row.addEventListener('click', (event) => {
                if (!event.target.closest('.jt-row-actions')) {
                    this.select(row);
                }
            });
        }

        row.appendChild(this.rowActions(value, path));

        if (this.matchPaths.has(path)) {
            row.classList.add('jt-match');
        }
        return item;
    }

    childList(value, path, depth) {
        const list = document.createElement('ul');
        list.className = 'jt-list';
        list.setAttribute('role', 'group');
        // Position of the indent guide: under the caret of the parent row.
        list.style.setProperty('--jt-depth', String(depth - 1));
        this.renderChildren(list, value, path, depth);
        return list;
    }

    toggle(item, value, path, depth, expand = undefined) {
        const expanded = this.expanded.has(path);
        const target = expand ?? !expanded;
        if (target === expanded) {
            return;
        }
        if (target) {
            this.expanded.add(path);
            item.appendChild(this.childList(value, path, depth));
        } else {
            this.expanded.delete(path);
            item.querySelector(':scope > .jt-list')?.remove();
        }
        item.setAttribute('aria-expanded', String(target));
    }

    renderContainerSummary(row, value) {
        const array = Array.isArray(value);
        let type = array ? null : shortTypeName(value[TYPE_KEY]);
        if (array && value.length > 0 && value.every((item) => isContainer(item) && !Array.isArray(item) && item[TYPE_KEY] === value[0][TYPE_KEY]) && value[0][TYPE_KEY]) {
            type = `${shortTypeName(value[0][TYPE_KEY])}[]`;
        }
        if (type) {
            const badge = document.createElement('span');
            badge.className = 'jt-type';
            badge.title = array ? value[0][TYPE_KEY] : value[TYPE_KEY];
            this.writeHighlighted(badge, type);
            row.appendChild(badge);
        }

        const count = array ? value.length : Object.keys(value).filter((key) => key !== TYPE_KEY).length;
        const meta = document.createElement('span');
        meta.className = 'jt-meta';
        meta.textContent = array ? `[${count}]` : `{${count}}`;
        meta.title = array ? `${count} item${count === 1 ? '' : 's'}` : `${count} member${count === 1 ? '' : 's'}`;
        row.appendChild(meta);

        const preview = this.previewText(value);
        if (preview) {
            const previewElement = document.createElement('span');
            previewElement.className = 'jt-preview';
            previewElement.textContent = preview;
            previewElement.title = preview;
            row.appendChild(previewElement);
        }
    }

    previewText(value) {
        if (Array.isArray(value)) {
            if (value.length > 0 && value.length <= 6 && value.every((item) => !isContainer(item))) {
                return value.map((item) => this.primitiveText(item)).join(', ');
            }
            return null;
        }

        const name = parameterName(value);
        if (name && !isContainer(value.Value) && value.Value !== undefined) {
            return `${name} = ${this.primitiveText(value.Value)}`;
        }

        const entries = Object.entries(value).filter(([key]) => key !== TYPE_KEY);
        if (entries.length > 0 && entries.length <= 4 && entries.every(([, item]) => !isContainer(item))) {
            return entries.map(([key, item]) => `${key}: ${this.primitiveText(item)}`).join(', ');
        }
        if (typeof value.Name === 'string' && value.Name) {
            return value.Name;
        }
        return null;
    }

    primitiveText(value) {
        if (typeof value === 'number') {
            return formatNumber(value);
        }
        if (typeof value === 'string') {
            return value.length > 60 ? `"${value.slice(0, 57)}…"` : `"${value}"`;
        }
        return String(value);
    }

    primitiveElement(value) {
        const element = document.createElement('span');
        if (value === null || value === undefined) {
            element.className = 'jt-value jt-null';
            this.writeHighlighted(element, 'null');
        } else if (typeof value === 'number') {
            element.className = 'jt-value jt-number';
            const text = formatNumber(value);
            this.writeHighlighted(element, text);
            if (text !== String(value)) {
                element.title = String(value);
            }
        } else if (typeof value === 'boolean') {
            element.className = 'jt-value jt-boolean';
            this.writeHighlighted(element, String(value));
        } else {
            element.className = 'jt-value jt-string';
            this.writeHighlighted(element, String(value));
        }
        return element;
    }

    rowActions(value, path) {
        const actions = document.createElement('span');
        actions.className = 'jt-row-actions';
        const copyValue = this.toolButton('copy', 'Copy value', () => {
            this.copyWithFeedback(copyValue, typeof value === 'string' ? value : JSON.stringify(value, null, 2));
        });
        const copyPath = this.toolButton('path', 'Copy path', () => this.copyWithFeedback(copyPath, path));
        copyValue.tabIndex = -1;
        copyPath.tabIndex = -1;
        actions.append(copyValue, copyPath);
        return actions;
    }

    writeHighlighted(element, text) {
        const term = this.searchTerm;
        if (!term) {
            element.textContent = text;
            return;
        }
        const lower = text.toLowerCase();
        let start = 0;
        let index = lower.indexOf(term);
        if (index < 0) {
            element.textContent = text;
            return;
        }
        while (index >= 0) {
            if (index > start) {
                element.appendChild(document.createTextNode(text.slice(start, index)));
            }
            const mark = document.createElement('mark');
            mark.textContent = text.slice(index, index + term.length);
            element.appendChild(mark);
            start = index + term.length;
            index = lower.indexOf(term, start);
        }
        if (start < text.length) {
            element.appendChild(document.createTextNode(text.slice(start)));
        }
    }

    // --- Selection and keyboard ----------------------------------------------------------------

    select(row, focus = true) {
        this.body.querySelectorAll('.jt-row.jt-selected').forEach((other) => {
            other.classList.remove('jt-selected');
            other.tabIndex = -1;
        });
        row.classList.add('jt-selected');
        row.tabIndex = 0;
        this.selectedPath = row.dataset.path;
        if (focus) {
            row.focus({ preventScroll: true });
        }
        row.scrollIntoView({ block: 'nearest' });
        this.updateStatus();
    }

    updateStatus() {
        if (this.preferences.raw || !this.selectedPath) {
            this.status.hidden = true;
            return;
        }
        this.status.hidden = false;
        this.status.replaceChildren();
        const text = document.createElement('span');
        text.className = 'jt-status-path';
        // The path is cut at its start (direction: rtl); the marks keep "$" and brackets in order.
        const mark = String.fromCharCode(0x200E);
        text.textContent = mark + this.selectedPath + mark;
        text.title = this.selectedPath;
        const copy = this.toolButton('copy', 'Copy path', () => this.copyWithFeedback(copy, this.selectedPath));
        copy.classList.add('jt-tool-inline');
        this.status.append(text, copy);
    }

    visibleRows() {
        return Array.from(this.body.querySelectorAll('.jt-row'));
    }

    valueAt(path) {
        if (path === '$') {
            return this.value;
        }
        // Paths are produced by childPath; walk them back with a small tokenizer.
        let current = this.value;
        const pattern = /\.([A-Za-z_$][\w$]*)|\[(\d+)\]|\[("(?:[^"\\]|\\.)*")\]/g;
        let match;
        while ((match = pattern.exec(path.slice(1))) !== null) {
            const key = match[1] ?? (match[2] !== undefined ? Number(match[2]) : JSON.parse(match[3]));
            current = current?.[key];
        }
        return current;
    }

    onKeyDown(event) {
        const row = event.target.closest?.('.jt-row');
        if (!row) {
            return;
        }
        const rows = this.visibleRows();
        const index = rows.indexOf(row);
        const item = row.parentElement;
        const path = row.dataset.path;
        const value = this.valueAt(path);
        const container = isContainer(value);
        const depth = Number(row.style.getPropertyValue('--jt-depth')) + 1;

        switch (event.key) {
            case 'ArrowDown':
                if (index < rows.length - 1) {
                    this.select(rows[index + 1]);
                }
                break;
            case 'ArrowUp':
                if (index > 0) {
                    this.select(rows[index - 1]);
                }
                break;
            case 'Home':
                this.select(rows[0]);
                break;
            case 'End':
                this.select(rows[rows.length - 1]);
                break;
            case 'ArrowRight':
                if (container) {
                    if (!this.expanded.has(path)) {
                        this.toggle(item, value, path, depth, true);
                    } else {
                        const first = item.querySelector(':scope > .jt-list .jt-row');
                        if (first) {
                            this.select(first);
                        }
                    }
                }
                break;
            case 'ArrowLeft':
                if (container && this.expanded.has(path)) {
                    this.toggle(item, value, path, depth, false);
                } else {
                    const parent = item.parentElement.closest('.jt-item');
                    const parentRow = parent?.querySelector(':scope > .jt-row');
                    if (parentRow) {
                        this.select(parentRow);
                    }
                }
                break;
            case 'Enter':
            case ' ':
                if (container) {
                    this.toggle(item, value, path, depth);
                }
                break;
            case 'c':
            case 'C':
                if ((event.ctrlKey || event.metaKey) && !window.getSelection()?.toString()) {
                    copyText(typeof value === 'string' ? value : JSON.stringify(value, null, 2));
                    break;
                }
                return;
            default:
                return;
        }
        event.preventDefault();
    }

    // --- Expand / collapse all -----------------------------------------------------------------

    expandAll() {
        let count = 0;
        const visit = (value, path) => {
            if (!isContainer(value) || count > EXPAND_ALL_NODE_LIMIT) {
                return;
            }
            if (path !== '$') {
                this.expanded.add(path);
            }
            const entries = this.visibleEntries(value);
            count += entries.length;
            for (const [key, child] of entries) {
                visit(child, childPath(path, key));
            }
        };
        visit(this.value, '$');
        this.refresh();
    }

    collapseAll() {
        this.expanded.clear();
        this.refresh();
    }

    // --- Search --------------------------------------------------------------------------------

    setSearch(term) {
        this.searchTerm = term.trim().toLowerCase();
        this.matchIndex = 0;
        if (this.searchTerm && !this.preferences.raw) {
            this.computeMatches();
            // Opens every branch leading to a match and widens array chunks to reach it.
            for (const path of this.matchPaths) {
                this.revealPath(path);
            }
        }
        this.refresh();
        if (this.searchTerm && this.matchRows.length > 0) {
            this.highlightCurrentMatch(true);
        }
    }

    computeMatches() {
        this.matchPaths = new Set();
        const term = this.searchTerm;
        if (!term || this.value === undefined) {
            return;
        }
        const visit = (value, path, key) => {
            if (this.matchPaths.size >= SEARCH_MATCH_LIMIT) {
                return;
            }
            let hit = key !== null && String(key).toLowerCase().includes(term);
            if (isContainer(value)) {
                if (!Array.isArray(value)) {
                    hit ||= (shortTypeName(value[TYPE_KEY]) ?? '').toLowerCase().includes(term);
                }
                if (hit && path !== '$') {
                    this.matchPaths.add(path);
                }
                for (const [childKey, child] of this.visibleEntries(value)) {
                    visit(child, childPath(path, childKey), childKey);
                }
            } else {
                const text = typeof value === 'number' ? formatNumber(value) : String(value);
                if (hit || text.toLowerCase().includes(term)) {
                    this.matchPaths.add(path);
                }
            }
        };
        visit(this.value, '$', null);
    }

    revealPath(path) {
        // Every ancestor prefix of the path ("$.a", "$.a.b[3]", ...) is expanded; an array index
        // beyond its chunk widens the parent's chunk limit.
        const pattern = /\.([A-Za-z_$][\w$]*)|\[(\d+)\]|\[("(?:[^"\\]|\\.)*")\]/g;
        let prefix = '$';
        let match;
        const segments = [];
        while ((match = pattern.exec(path.slice(1))) !== null) {
            segments.push(match);
        }
        segments.forEach((segment, index) => {
            if (segment[2] !== undefined) {
                const position = Number(segment[2]);
                const limit = this.chunkLimits.get(prefix) ?? CHUNK_SIZE;
                if (position >= limit) {
                    this.chunkLimits.set(prefix, Math.ceil((position + 1) / CHUNK_SIZE) * CHUNK_SIZE);
                }
            }
            prefix += segment[0];
            if (index < segments.length - 1) {
                this.expanded.add(prefix);
            }
        });
    }

    moveMatch(step) {
        if (this.matchRows.length === 0) {
            return;
        }
        this.matchIndex = (this.matchIndex + step + this.matchRows.length) % this.matchRows.length;
        this.highlightCurrentMatch(true);
        this.updateToolbarState();
    }

    highlightCurrentMatch(scroll) {
        this.matchRows.forEach((row, index) => row.classList.toggle('jt-match-current', index === this.matchIndex));
        const current = this.matchRows[this.matchIndex];
        if (current && scroll) {
            this.select(current, false);
        }
    }

    // --- Raw view ------------------------------------------------------------------------------

    renderRaw() {
        const pre = document.createElement('pre');
        pre.className = 'jt-raw';
        const append = (className, text) => {
            const span = document.createElement('span');
            span.className = className;
            span.textContent = text;
            pre.appendChild(span);
        };
        const write = (value, indent) => {
            if (value === null || value === undefined) {
                append('jt-null', 'null');
            } else if (typeof value === 'number') {
                append('jt-number', String(value));
            } else if (typeof value === 'boolean') {
                append('jt-boolean', String(value));
            } else if (typeof value === 'string') {
                append('jt-string', JSON.stringify(value));
            } else {
                const array = Array.isArray(value);
                const entries = array ? value.map((item, index) => [index, item]) : Object.entries(value);
                if (entries.length === 0) {
                    pre.appendChild(document.createTextNode(array ? '[]' : '{}'));
                    return;
                }
                pre.appendChild(document.createTextNode(array ? '[\n' : '{\n'));
                const inner = indent + '  ';
                entries.forEach(([key, child], index) => {
                    pre.appendChild(document.createTextNode(inner));
                    if (!array) {
                        append(key === TYPE_KEY ? 'jt-key jt-key-type' : 'jt-key', JSON.stringify(key));
                        pre.appendChild(document.createTextNode(': '));
                    }
                    write(child, inner);
                    pre.appendChild(document.createTextNode(index < entries.length - 1 ? ',\n' : '\n'));
                });
                pre.appendChild(document.createTextNode(indent + (array ? ']' : '}')));
            }
        };
        write(this.value, '');
        this.body.replaceChildren(pre);
        this.matchRows = [];
    }
}

// --- Pop-out window ----------------------------------------------------------------------------

let popOutOverlay = null;
let popOutTree = null;

/**
 * Opens the value in a large modal JSON inspector. When a source tree is given, its expanded
 * branches are carried over, so the window opens where the panel was.
 */
export function openJsonTreeWindow(value, reference = null, source = null) {
    if (!popOutOverlay) {
        popOutOverlay = document.createElement('div');
        popOutOverlay.className = 'gis-modal-overlay jt-window-overlay';
        popOutOverlay.hidden = true;

        const card = document.createElement('div');
        card.className = 'gis-card gis-modal gis-modal-wide jt-window';
        card.setAttribute('role', 'dialog');
        card.setAttribute('aria-modal', 'true');
        card.setAttribute('aria-label', 'Properties');

        const title = document.createElement('div');
        title.className = 'jt-window-title';
        const heading = document.createElement('h3');
        heading.className = 'gltf-card-title';
        heading.textContent = 'Properties';
        const close = document.createElement('button');
        close.type = 'button';
        close.className = 'gis-button';
        close.textContent = 'Close';
        close.addEventListener('click', closeJsonTreeWindow);
        title.append(heading, close);

        popOutTree = new JsonTree();
        card.append(title, popOutTree.element);
        popOutOverlay.appendChild(card);
        popOutOverlay.addEventListener('mousedown', (event) => {
            if (event.target === popOutOverlay) {
                closeJsonTreeWindow();
            }
        });
        document.addEventListener('keydown', (event) => {
            if (event.key === 'Escape' && !popOutOverlay.hidden) {
                closeJsonTreeWindow();
            }
        });
        document.body.appendChild(popOutOverlay);
    }

    if (source) {
        popOutTree.expanded = new Set(source.expanded);
        popOutTree.preferences = { ...source.preferences };
    }
    popOutTree.setData(value, reference);
    popOutOverlay.hidden = false;
    popOutTree.searchInput.focus();
}

export function closeJsonTreeWindow() {
    if (popOutOverlay) {
        popOutOverlay.hidden = true;
    }
}
