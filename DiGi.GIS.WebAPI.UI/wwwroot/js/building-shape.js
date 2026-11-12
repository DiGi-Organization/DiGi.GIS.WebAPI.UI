/**
 * Building Shape labelling page (DiGi.GIS.WebAPI.UI#86, part of DiGi.GIS#12).
 *
 * Shows the signed-in annotator's next unlabelled footprint of the fixed sample, drawn as an outline
 * beside its newest orthophoto, and records the shape class the annotator picks. The solver's own
 * class is never fetched while labelling (blind labelling); only Export CSV reads it.
 *
 * A class button (or its key) saves and advances. Skip advances without saving - the item comes
 * back after every other unlabelled one. Back reopens the previously labelled item of this visit;
 * picking a class there replaces the earlier label.
 */

(function () {
    'use strict';

    const shapeNames = {
        0: 'Undefined', 1: 'Square', 2: 'Rectangular', 3: 'Circular', 4: 'L', 5: 'H', 6: 'U',
        7: 'O', 8: 'E', 9: 'F', 10: 'T', 11: 'X', 12: 'Other'
    };

    // The enum member names as DiGi JSON may carry them (Circural is the member's spelling).
    const shapeValuesByName = {
        Undefined: 0, Square: 1, Rectangular: 2, Circural: 3, Circular: 3, L: 4, H: 5, U: 6,
        O: 7, E: 8, F: 9, T: 10, X: 11, Other: 12
    };

    const svgNamespace = 'http://www.w3.org/2000/svg';
    const svgSize = 1000;
    const svgMargin = 60;

    function getResolvedUrl(path) {
        const baseUrl = window.AppBaseUrl || '/';
        const cleanBase = baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl;
        return `${cleanBase}${path.startsWith('/') ? path : '/' + path}`;
    }

    const elements = {
        page: document.querySelector('.buildingshape-page'),
        ordinal: document.getElementById('buildingshape-ordinal'),
        county: document.getElementById('buildingshape-county'),
        reference: document.getElementById('buildingshape-reference'),
        progress: document.getElementById('buildingshape-progress'),
        progressText: document.getElementById('buildingshape-progress-text'),
        back: document.getElementById('buildingshape-back'),
        skip: document.getElementById('buildingshape-skip'),
        details: document.getElementById('buildingshape-details'),
        exportCsv: document.getElementById('buildingshape-export'),
        status: document.getElementById('buildingshape-status'),
        finished: document.getElementById('buildingshape-finished'),
        workspace: document.getElementById('buildingshape-workspace'),
        svg: document.getElementById('buildingshape-svg'),
        dimensions: document.getElementById('buildingshape-dimensions'),
        image: document.getElementById('buildingshape-image'),
        noImage: document.getElementById('buildingshape-noimage'),
        buttons: Array.from(document.querySelectorAll('.buildingshape-class')),
        note: document.getElementById('buildingshape-note')
    };

    // The item on screen ({ sampleKey, ordinal, countyId, reference, building2D }) or null when finished.
    let current = null;
    // Items labelled during this visit, oldest first: { item, shape, note }.
    const history = [];
    // The history entry on screen after Back, or null while showing a fresh (unlabelled) item.
    let historyIndex = null;
    let busy = false;
    // The object URL of the orthophoto on screen, revoked when the next one loads.
    let imageObjectUrl = null;

    // ---- helpers ---------------------------------------------------------------------------

    function pick(object, keys) {
        if (object === null || typeof object !== 'object') {
            return undefined;
        }

        for (const key of keys) {
            if (Object.prototype.hasOwnProperty.call(object, key)) {
                return object[key];
            }
        }

        return undefined;
    }

    function asArray(value) {
        return Array.isArray(value) ? value : [];
    }

    function parsePoints(value) {
        const points = [];
        for (const item of asArray(Array.isArray(value) ? value : pick(value, ['Points', 'points']))) {
            const x = Number(pick(item, ['X', 'x']));
            const y = Number(pick(item, ['Y', 'y']));
            if (Number.isFinite(x) && Number.isFinite(y)) {
                points.push([x, y]);
            }
        }

        return points;
    }

    function shapeValue(value) {
        if (typeof value === 'number') {
            return value;
        }

        if (typeof value === 'string') {
            if (Object.prototype.hasOwnProperty.call(shapeValuesByName, value)) {
                return shapeValuesByName[value];
            }

            const parsed = parseInt(value, 10);
            return isNaN(parsed) ? null : parsed;
        }

        return null;
    }

    function showStatus(message) {
        elements.status.textContent = message;
        elements.status.hidden = false;
    }

    function hideStatus() {
        elements.status.textContent = '';
        elements.status.hidden = true;
    }

    async function readJson(response) {
        try {
            return await response.json();
        } catch (error) {
            return null;
        }
    }

    async function getJson(path) {
        let response;
        try {
            response = await digiUser.fetchWithAuth(getResolvedUrl(path), { method: 'GET' });
        } catch (error) {
            return { status: 0, data: null };
        }

        return { status: response.status, data: response.ok ? await readJson(response) : null };
    }

    function failureMessage(what, status) {
        if (status === 0 || status === 503) {
            return `The service could not be reached (${what}).`;
        }

        if (status === 401) {
            return 'Your session has expired. Sign in again.';
        }

        return `${what} failed (status ${status}).`;
    }

    // ---- rendering ---------------------------------------------------------------------------

    function clearSvg() {
        while (elements.svg.firstChild) {
            elements.svg.removeChild(elements.svg.firstChild);
        }
    }

    /** Draws the outer ring and the holes of the footprint, fitted to the view box, north up. */
    function drawFootprint(building2D) {
        clearSvg();

        const face = pick(building2D, ['PolygonalFace2D', 'polygonalFace2D']);
        const outer = parsePoints(pick(face, ['ExternalEdge', 'externalEdge']));
        const holes = asArray(pick(face, ['InternalEdges', 'internalEdges'])).map(parsePoints).filter(function (ring) { return ring.length >= 3; });

        if (outer.length < 3) {
            const text = document.createElementNS(svgNamespace, 'text');
            text.setAttribute('x', String(svgSize / 2));
            text.setAttribute('y', String(svgSize / 2));
            text.setAttribute('text-anchor', 'middle');
            text.setAttribute('class', 'buildingshape-svg-missing');
            text.textContent = 'No footprint geometry';
            elements.svg.appendChild(text);
            elements.dimensions.textContent = 'Footprint not found - label it as "Cannot classify".';
            return;
        }

        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        for (const point of outer) {
            minX = Math.min(minX, point[0]); maxX = Math.max(maxX, point[0]);
            minY = Math.min(minY, point[1]); maxY = Math.max(maxY, point[1]);
        }

        const width = maxX - minX;
        const height = maxY - minY;
        const scale = (svgSize - 2 * svgMargin) / Math.max(width, height, 1e-6);
        const offsetX = (svgSize - width * scale) / 2;
        const offsetY = (svgSize - height * scale) / 2;

        function ringPath(ring) {
            return ring.map(function (point, index) {
                const x = offsetX + (point[0] - minX) * scale;
                const y = svgSize - (offsetY + (point[1] - minY) * scale);
                return `${index === 0 ? 'M' : 'L'}${x.toFixed(1)} ${y.toFixed(1)}`;
            }).join(' ') + ' Z';
        }

        const path = document.createElementNS(svgNamespace, 'path');
        path.setAttribute('d', [ringPath(outer)].concat(holes.map(ringPath)).join(' '));
        path.setAttribute('fill-rule', 'evenodd');
        path.setAttribute('class', 'buildingshape-outline');
        elements.svg.appendChild(path);

        elements.dimensions.textContent = `Footprint, about ${width.toFixed(1)} m × ${height.toFixed(1)} m (north up)`;
    }

    /**
     * Loads the newest orthophoto through the session-gated relay. Only a 404 means the footprint has no photo; any
     * other failure names its status, so an outage never reads as "No orthophoto" (Coding - WebAPI Contracts, section 3).
     */
    async function loadImage(item) {
        elements.image.hidden = true;
        elements.noImage.hidden = false;
        elements.noImage.textContent = 'Loading orthophoto…';
        elements.image.removeAttribute('src');
        if (imageObjectUrl !== null) {
            URL.revokeObjectURL(imageObjectUrl);
            imageObjectUrl = null;
        }

        const query = 'countyid=' + encodeURIComponent(item.countyId) + '&reference=' + encodeURIComponent(item.reference);
        const reference = item.reference;

        function isCurrent() {
            return current !== null && current.reference === reference;
        }

        function showNoImage(message) {
            if (isCurrent()) {
                elements.image.hidden = true;
                elements.noImage.hidden = false;
                elements.noImage.textContent = message;
            }
        }

        let response;
        try {
            response = await digiUser.fetchWithAuth(getResolvedUrl('/buildingshape/image?' + query), { method: 'GET' });
        } catch (error) {
            showNoImage('Orthophoto unavailable: the service could not be reached.');
            return;
        }

        if (response.status === 404) {
            showNoImage('No orthophoto');
            return;
        }

        if (!response.ok) {
            showNoImage(response.status === 503 ? 'Orthophoto unavailable: the service could not be reached.' : `Orthophoto unavailable (status ${response.status}).`);
            return;
        }

        const blob = await response.blob();
        if (!isCurrent()) {
            return;
        }

        imageObjectUrl = URL.createObjectURL(blob);
        elements.image.onload = function () {
            if (isCurrent()) {
                elements.image.hidden = false;
                elements.noImage.hidden = true;
            }
        };
        elements.image.onerror = function () {
            showNoImage('Orthophoto unavailable: the image could not be decoded.');
        };
        elements.image.src = imageObjectUrl;
    }

    function setPressed(shape) {
        for (const button of elements.buttons) {
            button.setAttribute('aria-pressed', String(Number(button.dataset.shape) === shape));
        }
    }

    function updateBack() {
        elements.back.disabled = historyIndex === null ? history.length === 0 : historyIndex === 0;
    }

    /** Shows an item: a fresh one (index null) or the history entry at index, with its label pressed. */
    function render(item, index) {
        current = item;
        historyIndex = index;

        const entry = index === null ? null : history[index];

        elements.finished.hidden = true;
        elements.workspace.hidden = false;
        elements.ordinal.textContent = String(item.ordinal + 1) + (entry ? ' (revising)' : '');
        elements.county.textContent = String(item.countyId);
        elements.reference.textContent = item.reference;
        elements.note.value = entry ? (entry.note || '') : '';
        setPressed(entry ? entry.shape : null);
        updateBack();

        drawFootprint(item.building2D);
        loadImage(item);
    }

    function showFinished() {
        current = null;
        historyIndex = null;
        elements.workspace.hidden = true;
        elements.finished.hidden = false;
        elements.ordinal.textContent = '–';
        elements.county.textContent = '–';
        elements.reference.textContent = '–';
        updateBack();
    }

    // ---- requests ----------------------------------------------------------------------------

    /** Reads and shows the progress; answers { labelled, total }, or { status } when it could not be read. */
    async function loadProgress() {
        const result = await getJson('/buildingshape/progress');
        if (result.data === null) {
            elements.progressText.textContent = 'progress unavailable';
            return { status: result.status };
        }

        const labelled = Number(pick(result.data, ['Labelled', 'labelled'])) || 0;
        const total = Number(pick(result.data, ['Total', 'total'])) || 0;
        elements.progress.max = Math.max(total, 1);
        elements.progress.value = labelled;
        elements.progressText.textContent = `${labelled} / ${total} labelled by you`;
        return { labelled: labelled, total: total };
    }

    async function loadNext(afterOrdinal) {
        const query = afterOrdinal === null || afterOrdinal === undefined ? '' : '?afterordinal=' + encodeURIComponent(afterOrdinal);
        const result = await getJson('/buildingshape/next' + query);
        if (result.data === null) {
            showStatus(failureMessage('Reading the next footprint', result.status));
            return;
        }

        const reference = pick(result.data, ['Reference', 'reference']);
        if (reference === null || reference === undefined) {
            // "No next item" is also what a sample that was never loaded, or a service without the route, answers;
            // the progress tells them apart, so only a real end of the sample shows the finished state.
            const progress = await loadProgress();
            if (progress.total === undefined) {
                showStatus(failureMessage('Reading the progress', progress.status));
                return;
            }

            if (progress.total === 0) {
                current = null;
                elements.workspace.hidden = true;
                showStatus(`The sample ${(elements.page && elements.page.dataset.sampleKey) || ''} has not been loaded yet. Nothing can be labelled.`);
                return;
            }

            showFinished();
            return;
        }

        hideStatus();
        render({
            sampleKey: pick(result.data, ['SampleKey', 'sampleKey']),
            ordinal: Number(pick(result.data, ['Ordinal', 'ordinal'])),
            countyId: Number(pick(result.data, ['CountyId', 'countyId'])),
            reference: String(reference),
            building2D: pick(result.data, ['Building2D', 'building2D'])
        }, null);
    }

    async function saveLabel(shape) {
        if (busy || current === null) {
            return;
        }

        busy = true;
        try {
            const item = current;
            const note = elements.note.value.trim();
            const body = { SampleKey: item.sampleKey, CountyId: item.countyId, Reference: item.reference, Shape: shape, Note: note === '' ? null : note };

            let response;
            try {
                response = await digiUser.fetchWithAuth(getResolvedUrl('/buildingshape/label'), {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(body)
                });
            } catch (error) {
                showStatus('The service could not be reached. The label was not saved.');
                return;
            }

            if (!response.ok) {
                showStatus(response.status === 400
                    ? 'The label was refused (status 400): labelling may be disabled on the server, or the item is not in the sample.'
                    : failureMessage('Saving the label', response.status) + ' The label was not saved.');
                return;
            }

            hideStatus();
            setPressed(shape);

            // A revision updates its own history entry; a fresh label is appended.
            if (historyIndex === null) {
                history.push({ item: item, shape: shape, note: note });
            } else {
                history[historyIndex] = { item: item, shape: shape, note: note };
            }

            // After a revision the annotator walks forward through the later entries, then back to fresh items.
            if (historyIndex !== null && historyIndex < history.length - 1) {
                await loadProgress();
                render(history[historyIndex + 1].item, historyIndex + 1);
                return;
            }

            await loadProgress();
            await loadNext(item.ordinal);
        } finally {
            busy = false;
        }
    }

    async function skip() {
        if (busy || current === null) {
            return;
        }

        busy = true;
        try {
            hideStatus();

            // Skipping a revision keeps its label and moves forward, as saving it would.
            if (historyIndex !== null && historyIndex < history.length - 1) {
                render(history[historyIndex + 1].item, historyIndex + 1);
                return;
            }

            await loadNext(current.ordinal);
        } finally {
            busy = false;
        }
    }

    function back() {
        if (busy) {
            return;
        }

        const index = historyIndex === null ? history.length - 1 : historyIndex - 1;
        if (index < 0) {
            return;
        }

        hideStatus();
        render(history[index].item, index);
    }

    // ---- export ------------------------------------------------------------------------------

    function csvCell(value) {
        const text = value === null || value === undefined ? '' : String(value);
        return /[",\r\n]/.test(text) ? '"' + text.replace(/"/g, '""') + '"' : text;
    }

    async function exportCsv() {
        if (!window.confirm('The export shows the solver’s own classes. Export only once your labelling is done. Continue?')) {
            return;
        }

        const results = await Promise.all([getJson('/buildingshape/sampleitems'), getJson('/buildingshape/labels')]);
        if (results[0].data === null || results[1].data === null) {
            showStatus(failureMessage('The export', results[0].data === null ? results[0].status : results[1].status));
            return;
        }

        const labelsByKey = {};
        for (const label of asArray(results[1].data)) {
            const key = `${pick(label, ['CountyId', 'countyId'])}:${pick(label, ['Reference', 'reference'])}`;
            (labelsByKey[key] = labelsByKey[key] || []).push(label);
        }

        const rows = [['ordinal', 'county_id', 'reference', 'solver_shape', 'shape', 'interpreter', 'note', 'updated_at']];
        for (const item of asArray(results[0].data)) {
            const countyId = pick(item, ['CountyId', 'countyId']);
            const reference = pick(item, ['Reference', 'reference']);
            const ordinal = pick(item, ['Ordinal', 'ordinal']);
            const solverShape = shapeNames[shapeValue(pick(item, ['SolverShape', 'solverShape']))] || '';
            const labels = labelsByKey[`${countyId}:${reference}`] || [];
            if (labels.length === 0) {
                rows.push([ordinal, countyId, reference, solverShape, '', '', '', '']);
                continue;
            }

            for (const label of labels) {
                rows.push([
                    ordinal, countyId, reference, solverShape,
                    shapeNames[shapeValue(pick(label, ['BuildingShape', 'buildingShape']))] || '',
                    pick(label, ['Interpreter', 'interpreter']),
                    pick(label, ['Note', 'note']),
                    pick(label, ['DateTime', 'dateTime'])
                ]);
            }
        }

        const csv = rows.map(function (row) { return row.map(csvCell).join(','); }).join('\r\n');
        const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8' });
        const link = document.createElement('a');
        link.href = URL.createObjectURL(blob);
        link.download = 'building-shape-labels.csv';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(link.href);
    }

    // ---- wiring ------------------------------------------------------------------------------

    for (const button of elements.buttons) {
        button.setAttribute('aria-pressed', 'false');
        button.addEventListener('click', function () {
            saveLabel(Number(button.dataset.shape));
        });
    }

    elements.skip.addEventListener('click', skip);
    elements.back.addEventListener('click', back);
    elements.exportCsv.addEventListener('click', exportCsv);
    elements.details.addEventListener('click', function () {
        if (current === null) {
            return;
        }

        const query = 'reference=' + encodeURIComponent(current.reference) + '&countyid=' + encodeURIComponent(current.countyId);
        window.open(getResolvedUrl('/building2D/detailsbyreference?' + query), '_blank');
    });

    document.addEventListener('keydown', function (event) {
        if (event.ctrlKey || event.metaKey || event.altKey) {
            return;
        }

        // Typing a note never triggers a shortcut.
        const target = event.target;
        if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA')) {
            return;
        }

        const key = event.key.toLowerCase();
        if (key === ' ') {
            event.preventDefault();
            skip();
            return;
        }

        if (key === 'b') {
            back();
            return;
        }

        const button = elements.buttons.find(function (candidate) { return candidate.dataset.key === key; });
        if (button) {
            event.preventDefault();
            saveLabel(Number(button.dataset.shape));
        }
    });

    loadProgress();
    loadNext(null);
})();
