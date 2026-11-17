/*
 * The GIS glue of the AI assistant (DiGi.GIS.WebAPI.UI#88, #90): the floating button and the panel around the
 * DiGi.Harness chat widget (harness-chat-core.js, copied from DiGi.Harness.WebAPI - edit it there), the GIS Assistant
 * page (Views/Home/Assistant.cshtml) and the home page's "GIS Assistant" button.
 *
 * The layout renders the button and the panel for signed-in visitors only; the button (and the home page button) is
 * shown once harness/harnesschat/configuration reports the assistant enabled for this user. Requests go through
 * digiUser.fetchWithAuth, so an expired token is refreshed once before a 401 is reported.
 *
 * Opening the assistant in a new window continues the conversation: its id lives in sessionStorage
 * (gis.harness.session), which a browser copies into a window opened by window.open without noopener. The server
 * binds every session to its user, so the copied id reaches nobody else's conversation.
 */
(function () {
    'use strict';

    if (!window.DiGiHarnessChat) {
        return;
    }

    const host = document.getElementById('gis-harness-chat');
    const notice = document.getElementById('gis-harness-notice');
    const homeButton = document.getElementById('gis-home-assistant');

    const appBaseUrl = (window.AppBaseUrl || '').replace(/\/$/, '');
    const baseUrl = `${appBaseUrl}/harness/harnesschat`;
    const assistantUrl = `${appBaseUrl}/assistant`;
    const assistantPath = new URL(assistantUrl, window.location.href).pathname;

    // One named window: a second "open" focuses it rather than spawning another (or reloading a running answer).
    const windowName = 'gis-assistant';
    const windowFeatures = 'popup,width=480,height=760';
    const openKey = 'gis.harness.open';

    // digiUser is a top-level const of user.js: a global binding, but not a property of window.
    const fetchFunction = (url, init) => (typeof digiUser !== 'undefined' && digiUser.fetchWithAuth)
        ? digiUser.fetchWithAuth(url, init)
        : window.fetch(url, init);

    function createChat(container) {
        return window.DiGiHarnessChat.create(container, {
            baseUrl: baseUrl,
            fetch: fetchFunction,
            storageKey: 'gis.harness',
            title: 'GIS Assistant',
            placeholder: 'Ask about buildings, counties, addresses…',
            emptyText: 'Ask about buildings, counties, addresses or building data. The assistant answers by calling the GIS Web API with your account and shows every call it makes.',
        });
    }

    function readOpen() {
        try {
            return window.sessionStorage.getItem(openKey) === 'true';
        } catch (error) {
            return false;
        }
    }

    function writeOpen(open) {
        try {
            window.sessionStorage.setItem(openKey, open ? 'true' : 'false');
        } catch (error) {
            // Only the open state is lost.
        }
    }

    function showNotice(text) {
        if (!notice) {
            return;
        }

        notice.textContent = text || '';
        notice.hidden = !text;
    }

    /**
     * Opens the GIS Assistant page in its own window, or focuses the one already open.
     *
     * The window is opened blank first so an existing one can be told apart from a new one: a new window (or one
     * navigated elsewhere) is pointed at the assistant, the assistant itself is only focused.
     *
     * @returns {boolean} False when the browser blocked the window.
     */
    function openAssistantWindow() {
        let assistantWindow = null;
        try {
            assistantWindow = window.open('', windowName, windowFeatures);
        } catch (error) {
            assistantWindow = null;
        }

        if (!assistantWindow) {
            return false;
        }

        let path = null;
        try {
            path = assistantWindow.location.pathname;
        } catch (error) {
            // Navigated to another origin: not readable, but still ours to navigate.
            path = null;
        }

        if (path !== assistantPath) {
            assistantWindow.location.href = assistantUrl;
        }

        assistantWindow.focus();
        return true;
    }

    // The GIS Assistant page: the chat is the page.
    function startPage(container) {
        // Copied from the opener, the panel's open flag would pop the panel open on the next page of this window.
        writeOpen(false);

        const chat = createChat(container);
        chat.loadConfiguration().then(configuration => {
            if (!configuration || !configuration.Enabled) {
                container.hidden = true;
                showNotice(configuration
                    ? 'The GIS Assistant is not available for this account.'
                    : 'The GIS Assistant could not be loaded. Reload the page to try again.');
                return;
            }

            chat.restoreSession();
            chat.focus();
        });
    }

    // Every other page: the floating button, the panel and, on the home page, the "GIS Assistant" button.
    function startPanel() {
        const toggle = document.getElementById('gis-harness-toggle');
        const panel = document.getElementById('gis-harness-panel');
        if (!toggle || !panel || !host) {
            return;
        }

        const closeButton = panel.querySelector('.gis-harness-close');
        const popoutButton = panel.querySelector('.gis-harness-popout');
        const chat = createChat(host);

        let restored = false;

        function setOpen(open) {
            panel.hidden = !open;
            toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
            document.body.classList.toggle('gis-harness-open', open);
            if (open) {
                if (!restored) {
                    restored = true;
                    chat.restoreSession();
                }

                chat.focus();
            } else {
                showNotice(null);
            }

            writeOpen(open);
        }

        toggle.addEventListener('click', () => setOpen(panel.hidden));

        if (closeButton) {
            closeButton.addEventListener('click', () => {
                setOpen(false);
                toggle.focus();
            });
        }

        if (popoutButton) {
            popoutButton.addEventListener('click', () => {
                // A turn in progress streams into this panel: moving now would leave its answer behind.
                if (chat.isRunning()) {
                    return;
                }

                if (!openAssistantWindow()) {
                    showNotice('The browser blocked the new window. Allow pop-ups for this site and try again.');
                    return;
                }

                // The conversation may move on in the new window: reopening the panel reads it again.
                restored = false;
                setOpen(false);
                toggle.focus();
            });
        }

        if (homeButton) {
            homeButton.addEventListener('click', () => {
                if (!openAssistantWindow()) {
                    window.location.href = assistantUrl;
                }
            });
        }

        document.addEventListener('keydown', event => {
            if (event.key === 'Escape' && !panel.hidden && !chat.isRunning()) {
                setOpen(false);
                toggle.focus();
            }
        });

        chat.loadConfiguration().then(configuration => {
            if (!configuration || !configuration.Enabled) {
                return;
            }

            toggle.hidden = false;
            if (homeButton) {
                homeButton.hidden = false;
            }

            if (readOpen()) {
                setOpen(true);
            }
        });
    }

    if (host && host.dataset.standalone === 'true') {
        startPage(host);
    } else {
        startPanel();
    }
})();
