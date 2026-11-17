/*
 * The GIS glue of the AI assistant (DiGi.GIS.WebAPI.UI#88): the floating button and the panel around the
 * DiGi.Harness chat widget (harness-chat-core.js, copied from DiGi.Harness.WebAPI - edit it there).
 *
 * The layout renders the button and the panel for signed-in visitors only; the button is shown once
 * harness/harnesschat/configuration reports the assistant enabled for this user. Requests go through
 * digiUser.fetchWithAuth, so an expired token is refreshed once before a 401 is reported.
 */
(function () {
    'use strict';

    const toggle = document.getElementById('gis-harness-toggle');
    const panel = document.getElementById('gis-harness-panel');
    const host = document.getElementById('gis-harness-chat');
    if (!toggle || !panel || !host || !window.DiGiHarnessChat) {
        return;
    }

    const closeButton = panel.querySelector('.gis-harness-close');
    const baseUrl = `${(window.AppBaseUrl || '').replace(/\/$/, '')}/harness/harnesschat`;
    // digiUser is a top-level const of user.js: a global binding, but not a property of window.
    const fetchFunction = (url, init) => (typeof digiUser !== 'undefined' && digiUser.fetchWithAuth)
        ? digiUser.fetchWithAuth(url, init)
        : window.fetch(url, init);

    const chat = window.DiGiHarnessChat.create(host, {
        baseUrl: baseUrl,
        fetch: fetchFunction,
        storageKey: 'gis.harness',
        title: 'GIS assistant',
        placeholder: 'Ask about buildings, counties, addresses…',
        emptyText: 'Ask about buildings, counties, addresses or building data. The assistant answers by calling the GIS Web API with your account and shows every call it makes.',
    });

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
        }

        try {
            window.sessionStorage.setItem('gis.harness.open', open ? 'true' : 'false');
        } catch (error) {
            // Only the open state is lost.
        }
    }

    toggle.addEventListener('click', () => setOpen(panel.hidden));

    if (closeButton) {
        closeButton.addEventListener('click', () => {
            setOpen(false);
            toggle.focus();
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

        let open = false;
        try {
            open = window.sessionStorage.getItem('gis.harness.open') === 'true';
        } catch (error) {
            open = false;
        }

        if (open) {
            setOpen(true);
        }
    });
})();
