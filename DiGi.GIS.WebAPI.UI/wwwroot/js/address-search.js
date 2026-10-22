/*
 * Address search of the Building Viewer start page (DiGi.GIS.WebAPI.UI#76).
 *
 * Types go to this application's own relay (AddressPointController), which forwards them to the GIS Web
 * API's buildingsbyaddress. The query names - text, housenumber - are that contract; the API ignores an
 * unknown name silently, so they are kept in step by hand.
 */
function initAddressSearch(options) {
    const searchDelay = 300;

    const textInput = document.getElementById('address-search-text');
    const houseNumberInput = document.getElementById('address-search-housenumber');
    const form = document.getElementById('address-search-form');
    const status = document.getElementById('address-search-status');
    const list = document.getElementById('address-search-results');
    if (textInput === null || houseNumberInput === null || form === null || status === null || list === null) {
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

    function label(match) {
        const point = match.AddressPoint || {};
        const street = [point.StreetName, point.HouseNumber].filter(Boolean).join(' ');
        const locality = [point.PostalCode, point.LocalityName].filter(Boolean).join(' ');
        return [street, locality].filter(Boolean).join(', ');
    }

    function setStatus(text, isError) {
        status.textContent = text;
        status.classList.toggle('text-danger', isError === true);
    }

    function clearList() {
        rows = [];
        activeIndex = -1;
        list.innerHTML = '';
        list.hidden = true;
        textInput.setAttribute('aria-expanded', 'false');
        textInput.removeAttribute('aria-activedescendant');
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
        const items = list.children;
        for (let i = 0; i < items.length; i++) {
            const selected = i === index;
            items[i].setAttribute('aria-selected', selected ? 'true' : 'false');
            items[i].classList.toggle('gis-address-active', selected);
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
            setStatus('This address has no location to open.', true);
            return;
        }
        window.location.href = options.openUrl
            + '?reference=' + encodeURIComponent(match.Reference)
            + '&x=' + encodeURIComponent(x)
            + '&y=' + encodeURIComponent(y);
    }

    function render(matches) {
        rows = matches;
        list.innerHTML = '';
        matches.forEach(function (match, index) {
            const item = document.createElement('li');
            item.id = 'address-search-option-' + index;
            item.setAttribute('role', 'option');
            item.setAttribute('aria-selected', 'false');
            item.className = 'gis-address-option';
            item.textContent = label(match);
            item.addEventListener('mousedown', function (event) {
                event.preventDefault();
                open(index);
            });
            list.appendChild(item);
        });
        list.hidden = false;
        textInput.setAttribute('aria-expanded', 'true');
        setActive(-1);
    }

    async function search() {
        const text = textInput.value.trim();
        abort();
        if (text.length < options.minimumLength) {
            clearList();
            setStatus(text.length === 0 ? '' : 'Type at least ' + options.minimumLength + ' characters to search.', false);
            return;
        }

        const current = sequence;
        controller = new AbortController();
        let query = '?text=' + encodeURIComponent(text);
        const houseNumber = houseNumberInput.value.trim();
        if (houseNumber.length > 0) {
            query += '&housenumber=' + encodeURIComponent(houseNumber);
        }

        setStatus('Searching...', false);
        try {
            const response = await fetch(options.searchUrl + query, { signal: controller.signal });
            if (current !== sequence) {
                return;
            }
            if (response.status === 204) {
                clearList();
                setStatus('No matching address found.', false);
                return;
            }
            if (!response.ok) {
                clearList();
                setStatus(response.status === 503 ? 'The address service is unreachable.' : 'The address search failed.', true);
                return;
            }
            const matches = await response.json();
            if (current !== sequence) {
                return;
            }
            if (!Array.isArray(matches) || matches.length === 0) {
                clearList();
                setStatus('No matching address found.', false);
                return;
            }
            setStatus(matches.length + (matches.length === 1 ? ' match.' : ' matches.'), false);
            render(matches);
        } catch (error) {
            if (error && error.name === 'AbortError') {
                return;
            }
            if (current === sequence) {
                clearList();
                setStatus('The address search failed.', true);
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
            abort();
            clearList();
            setStatus('', false);
        }
    }

    textInput.addEventListener('keydown', onKey);
    houseNumberInput.addEventListener('keydown', onKey);
}
