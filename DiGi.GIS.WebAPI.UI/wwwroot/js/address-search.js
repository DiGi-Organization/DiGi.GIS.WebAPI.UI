/*
 * Address search of the Buildings start page (DiGi.GIS.WebAPI.UI#76, moved from the 3D Viewer start page by #79).
 *
 * Types go to this application's own relay (AddressPointController), which forwards them to the GIS Web
 * API's buildingsbyaddress. The query names - text, housenumber - are that contract; the API ignores an
 * unknown name silently, so they are kept in step by hand.
 *
 * The results box mirrors the Select Area prompt of the Typology page (#80): it is always shown, and every
 * state - the minimum-length hint, searching, no matches, a refusal - is written inside it with the same
 * classes, so the two searches look and read alike.
 */
function initAddressSearch(options) {
    const searchDelay = 300;

    const textInput = document.getElementById('address-search-text');
    const houseNumberInput = document.getElementById('address-search-housenumber');
    const form = document.getElementById('address-search-form');
    const list = document.getElementById('address-search-results');
    if (textInput === null || houseNumberInput === null || form === null || list === null) {
        return;
    }

    let timer = null;
    let controller = null;
    // Bumped on every new search, so a response that outlives its keystroke is dropped instead of
    // overwriting the newer list.
    let sequence = 0;
    let rows = [];
    let activeIndex = -1;

    function coordinate(location, name, index) {
        if (location === null || location === undefined) {
            return null;
        }
        let value = location[name];
        if (value === undefined) {
            value = location[name.toLowerCase()];
        }
        if (value === undefined && Array.isArray(location.Values)) {
            value = location.Values[index];
        }
        if (value === undefined && Array.isArray(location.Coordinates)) {
            value = location.Coordinates[index];
        }
        return typeof value === 'number' && isFinite(value) ? value : null;
    }

    // The label's parts, read as a breadcrumb like the Select Area rows: street and number, then postal code
    // and locality.
    function labelParts(match) {
        const point = match.AddressPoint || {};
        const street = [point.StreetName, point.HouseNumber].filter(Boolean).join(' ');
        const locality = [point.PostalCode, point.LocalityName].filter(Boolean).join(' ');
        return [street, locality].filter(Boolean);
    }

    // Every message state is a non-result state: the rows are gone, and with them any selection, so Enter
    // must not open a row the visitor can no longer see.
    function showMessage(text, isError) {
        rows = [];
        activeIndex = -1;
        textInput.setAttribute('aria-expanded', 'false');
        textInput.removeAttribute('aria-activedescendant');

        const message = document.createElement('div');
        message.className = isError === true ? 'gis-empty-state gis-typology-load-error' : 'gis-empty-state';
        message.textContent = text;
        list.replaceChildren(message);
    }

    function showIdle() {
        showMessage('Type at least ' + options.minimumLength + ' characters to search.', false);
    }

    function showSearching() {
        rows = [];
        activeIndex = -1;
        textInput.setAttribute('aria-expanded', 'false');
        textInput.removeAttribute('aria-activedescendant');

        const loader = document.createElement('div');
        loader.className = 'gis-loader';
        const caption = document.createElement('p');
        caption.className = 'gis-loader-text';
        caption.textContent = 'Searching addresses…';
        list.replaceChildren(loader, caption);
    }

    function abort() {
        if (timer !== null) {
            clearTimeout(timer);
            timer = null;
        }
        if (controller !== null) {
            controller.abort();
            controller = null;
        }
        sequence++;
    }

    function setActive(index) {
        activeIndex = index;
        const items = list.querySelectorAll('.gis-typology-result-row');
        for (let i = 0; i < items.length; i++) {
            const selected = i === index;
            items[i].setAttribute('aria-selected', selected ? 'true' : 'false');
            items[i].classList.toggle('gis-typology-result-selected', selected);
        }
        if (index >= 0 && index < items.length) {
            textInput.setAttribute('aria-activedescendant', items[index].id);
            items[index].scrollIntoView({ block: 'nearest' });
        } else {
            textInput.removeAttribute('aria-activedescendant');
        }
    }

    function open(index) {
        if (index < 0 || index >= rows.length) {
            return;
        }
        const match = rows[index];
        const x = coordinate(match.AddressPoint ? match.AddressPoint.Location : null, 'X', 0);
        const y = coordinate(match.AddressPoint ? match.AddressPoint.Location : null, 'Y', 1);
        if (!match.Reference || x === null || y === null) {
            showMessage('This address has no location to open.', true);
            return;
        }
        window.location.href = options.openUrl
            + '?reference=' + encodeURIComponent(match.Reference)
            + '&x=' + encodeURIComponent(x)
            + '&y=' + encodeURIComponent(y);
    }

    function render(matches) {
        const items = matches.map(function (match, index) {
            const item = document.createElement('div');
            item.id = 'address-search-option-' + index;
            item.setAttribute('role', 'option');
            item.setAttribute('aria-selected', 'false');
            item.className = 'gis-typology-result-row';

            const breadcrumb = document.createElement('span');
            breadcrumb.className = 'gis-path-breadcrumb';
            labelParts(match).forEach(function (part, partIndex) {
                if (partIndex > 0) {
                    const separator = document.createElement('span');
                    separator.className = 'breadcrumb-separator';
                    separator.textContent = '›';
                    breadcrumb.appendChild(separator);
                }
                const wrapper = document.createElement('span');
                wrapper.className = 'breadcrumb-item-wrapper';
                wrapper.textContent = part;
                breadcrumb.appendChild(wrapper);
            });
            item.appendChild(breadcrumb);

            // mousedown, not click, so the inputs keep focus and the open is not lost to a blur.
            item.addEventListener('mousedown', function (event) {
                event.preventDefault();
                open(index);
            });
            return item;
        });

        rows = matches;
        list.replaceChildren.apply(list, items);
        textInput.setAttribute('aria-expanded', 'true');
        setActive(-1);
    }

    async function search() {
        const text = textInput.value.trim();
        abort();
        if (text.length < options.minimumLength) {
            showIdle();
            return;
        }

        const current = sequence;
        controller = new AbortController();
        let query = '?text=' + encodeURIComponent(text);
        const houseNumber = houseNumberInput.value.trim();
        if (houseNumber.length > 0) {
            query += '&housenumber=' + encodeURIComponent(houseNumber);
        }

        showSearching();
        try {
            const response = await fetch(options.searchUrl + query, { signal: controller.signal });
            if (current !== sequence) {
                return;
            }
            if (response.status === 204) {
                showMessage('No matching address found.', false);
                return;
            }
            if (!response.ok) {
                showMessage(response.status === 503 ? 'The address service is unreachable.' : 'The address search failed.', true);
                return;
            }
            const matches = await response.json();
            if (current !== sequence) {
                return;
            }
            if (!Array.isArray(matches) || matches.length === 0) {
                showMessage('No matching address found.', false);
                return;
            }
            render(matches);
        } catch (error) {
            if (error && error.name === 'AbortError') {
                return;
            }
            // A dead connection or an undecodable body is a refusal, not an empty result.
            if (current === sequence) {
                showMessage('The address search failed.', true);
            }
        }
    }

    function schedule() {
        abort();
        timer = setTimeout(search, searchDelay);
    }

    textInput.addEventListener('input', schedule);
    houseNumberInput.addEventListener('input', schedule);
    // A form with two text inputs and no submit button is not submitted by Enter, so the key is handled
    // here; the submit listener only keeps a stray submission from navigating away.
    form.addEventListener('submit', function (event) {
        event.preventDefault();
    });

    function onKey(event) {
        if (event.key === 'Enter') {
            event.preventDefault();
            if (activeIndex >= 0) {
                open(activeIndex);
            } else if (rows.length > 0) {
                open(0);
            } else {
                abort();
                search();
            }
        } else if (event.key === 'ArrowDown' && rows.length > 0) {
            event.preventDefault();
            setActive((activeIndex + 1) % rows.length);
        } else if (event.key === 'ArrowUp' && rows.length > 0) {
            event.preventDefault();
            setActive(activeIndex <= 0 ? rows.length - 1 : activeIndex - 1);
        } else if (event.key === 'Escape') {
            // The box stays on the page, so Escape only drops the highlighted row.
            setActive(-1);
        }
    }

    textInput.addEventListener('keydown', onKey);
    houseNumberInput.addEventListener('keydown', onKey);

    showIdle();
}
