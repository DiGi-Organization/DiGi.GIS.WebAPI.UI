/**
 * Orto Data area mode - the Full-interpretation test areas (issue #84).
 *
 * A separate page half from ortodata.js, loaded only when the page was opened with
 * ?mode=area: that script never runs here and this one never runs there, so the
 * building mode's behaviour is untouched.
 *
 * One epoch card per year the area relays lists; every card carries an SVG overlay
 * of the area's footprints, its sampled cells and the polygons drawn by hand. A click
 * on any of those polygons selects it for one epoch and marks it present (0),
 * absent (1) or indeterminate (2) - the state vocabulary of DiGi.GIS.ML#19, sent as
 * integer codes. Other interpreters' rows are shown on the same control but are never
 * editable from here; one CSV export carries every interpreter's rows, which is the
 * agreement (kappa) input of the tracking issue.
 *
 * The sibling contract this speaks to (DiGi.GIS.WebAPI#74) pins the route and query
 * names but not yet the JSON casing of every body it returns, so the readers below
 * accept both the camelCase and the PascalCase spelling of each field.
 */

(function () {
    'use strict';

    const svgSize = 1000;            // fixed viewBox edge; the world -> svg mapping is linear over the bounding box
    const closeTolerancePx = 8;      // screen-pixel radius in which a click on the first vertex closes the polygon
    const stateNames = ['Present', 'Absent', 'Indeterminate'];   // index = the integer code on the wire
    const defaultMaxAreaSide = 2000; // mirrors DiGi.GIS.WebAPI.UI.Constants.Default.OrtoAreaMaximumSide if the data attribute is missing

    /**
     * Resolves a root-relative path relative to the application's base URL.
     * Same helper as ortodata.js; kept local so the two scripts stay independent.
     */
    function getResolvedUrl(path) {
        const baseUrl = window.AppBaseUrl || '/';
        const cleanBase = baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl;
        return `${cleanBase}${path.startsWith('/') ? path : '/' + path}`;
    }

    const elements = {
        panel: document.getElementById('ortodata-area-panel'),
        x1: document.getElementById('ortodata-area-x1'),
        y1: document.getElementById('ortodata-area-y1'),
        x2: document.getElementById('ortodata-area-x2'),
        y2: document.getElementById('ortodata-area-y2'),
        load: document.getElementById('ortodata-area-load'),
        draw: document.getElementById('ortodata-area-draw'),
        close: document.getElementById('ortodata-area-close'),
        export: document.getElementById('ortodata-area-export'),
        status: document.getElementById('ortodata-area-status'),
        marking: document.getElementById('ortodata-area-marking'),
        grid: document.getElementById('ortodata-area-grid')
    };

    // The loaded area. bbox is the normalized query box the imagery and the overlay are framed by;
    // areaKey and cells come from the area items, footprints from the building read.
    const state = {
        bbox: null,
        areaKey: '',
        cells: [],
        objects: [],       // drawn polygons: { objectId, cellIndex, points }
        footprints: [],    // { reference, points }
        labels: [],        // every interpreter's rows, normalized below
        years: [],
        interpreter: null,      // the signed-in e-mail, lowercased; null = identity unreadable -> read-only
        identityState: 'pending',
        selection: null,   // { kind: 'footprint'|'drawn', reference?, objectId?, year }
        drawMode: false,
        drawVertices: [],
        drawCardYear: null,
        maxAreaSide: defaultMaxAreaSide,
        busy: false,
        posting: false
    };

    function showStatus(message) {
        elements.status.textContent = message;
        elements.status.hidden = false;
    }

    function hideStatus() {
        elements.status.hidden = true;
        elements.status.textContent = '';
    }

    // ---- reading the sibling payloads (casing-tolerant) ------------------------------------

    /**
     * Returns the first own property of the object among the given spellings, preserving an own
     * null (a meaningful "unknown county id") and reporting absence as undefined.
     */
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

    /** Invariant number text for the wire, a URL or a CSV cell; anything not finite becomes empty. */
    function num(value) {
        return Number.isFinite(value) ? String(value) : '';
    }

    function parsePoint(value) {
        const x = pick(value, ['X', 'x']);
        const y = pick(value, ['Y', 'y']);
        if (!Number.isFinite(Number(x)) || !Number.isFinite(Number(y))) {
            return null;
        }

        return [Number(x), Number(y)];
    }

    /** Accepts [[x, y], ...], a point array or a DiGi Polygon2D ({ Points: [...] }). */
    function parsePoints(value) {
        if (value === null || value === undefined) {
            return [];
        }

        if (Array.isArray(value)) {
            const points = [];
            for (const item of value) {
                const point = parsePoint(item);
                if (point !== null) {
                    points.push(point);
                } else if (Array.isArray(item) && item.length >= 2 && Number.isFinite(Number(item[0])) && Number.isFinite(Number(item[1]))) {
                    points.push([Number(item[0]), Number(item[1])]);
                }
            }

            return points;
        }

        return parsePoints(pick(value, ['Points', 'points']));
    }

    function parseBoundingBox(value) {
        const min = parsePoint(pick(value, ['Min', 'min']));
        const max = parsePoint(pick(value, ['Max', 'max']));
        if (min === null || max === null) {
            return null;
        }

        return { x1: Math.min(min[0], max[0]), y1: Math.min(min[1], max[1]), x2: Math.max(min[0], max[0]), y2: Math.max(min[1], max[1]) };
    }

    function normaliseLabelRow(row) {
        return {
            areaKey: pick(row, ['areaKey', 'AreaKey']) || '',
            countyId: pick(row, ['countyId', 'CountyId']),
            reference: pick(row, ['reference', 'Reference']),
            objectId: pick(row, ['objectId', 'ObjectId']),
            epochYear: Number(pick(row, ['epochYear', 'EpochYear'])),
            state: Number(pick(row, ['state', 'State'])),
            observationDateMin: pick(row, ['observationDateMin', 'ObservationDateMin']),
            observationDateMax: pick(row, ['observationDateMax', 'ObservationDateMax']),
            interpreter: pick(row, ['interpreter', 'Interpreter']),
            note: pick(row, ['note', 'Note']),
            updatedAt: pick(row, ['updatedAt', 'UpdatedAt'])
        };
    }

    /**
     * Takes the /ortodata/area/items payload - the area with its cells and the drawn objects,
     * optionally wrapped with the labels - and folds it into the state. The wrapper keys are
     * accepted in both casings and the payload may also be the area itself.
     */
    function applyAreaItems(payload) {
        if (payload === null || typeof payload !== 'object' || Array.isArray(payload)) {
            return false;
        }

        const wrappedArea = pick(payload, ['area', 'Area']);
        const area = (wrappedArea !== null && wrappedArea !== undefined) ? wrappedArea
            : (pick(payload, ['areaKey', 'AreaKey']) !== undefined ? payload : null);
        if (area === null) {
            return false;
        }

        state.areaKey = pick(area, ['areaKey', 'AreaKey']) || '';

        const cells = asArray(pick(area, ['cells', 'Cells']));
        state.cells = [];
        for (const cell of cells) {
            const index = pick(cell, ['index', 'Index']);
            const boundingBox = parseBoundingBox(pick(cell, ['boundingBox2D', 'BoundingBox2D']));
            if (boundingBox !== null && Number.isFinite(Number(index))) {
                state.cells.push({ index: Number(index), bbox: boundingBox });
            }
        }

        const objects = asArray(pick(payload, ['objects', 'Objects', 'ortoAreaObjects', 'OrtoAreaObjects']));
        state.objects = [];
        for (const object of objects) {
            const objectId = pick(object, ['objectId', 'ObjectId']);
            if (objectId === null || objectId === undefined) {
                continue;
            }

            const polygon = pick(object, ['polygon2D', 'Polygon2D', 'polygon', 'Polygon']);
            const cellIndex = pick(object, ['cellIndex', 'CellIndex']);
            state.objects.push({
                objectId: String(objectId),
                // Normalized once: null stays "outside every sampled cell", anything else must be a
                // number for the cell highlight and the export's cell_id to line up.
                cellIndex: cellIndex === null || cellIndex === undefined || !Number.isFinite(Number(cellIndex)) ? null : Number(cellIndex),
                points: parsePoints(polygon)
            });
        }

        // The labels carried alongside (if any) are folded in; the dedicated label read stays the
        // authoritative list and overwrites this when it has been made.
        const labels = asArray(pick(payload, ['labels', 'Labels', 'ortoAreaLabels', 'OrtoAreaLabels']));
        if (labels.length > 0) {
            state.labels = labels.map(normaliseLabelRow);
        }

        return true;
    }

    /** The footprint read answers a bare Building2D list; only the outline and identity are kept. */
    function applyFootprints(payload) {
        state.footprints = [];
        for (const building of asArray(payload)) {
            const reference = pick(building, ['Reference', 'reference']);
            const geometry = pick(building, ['PolygonalFace2D', 'polygonalFace2D']);
            const edge = pick(geometry, ['ExternalEdge', 'externalEdge']);
            const points = parsePoints(edge);
            if (reference === null || reference === undefined || points.length < 3) {
                continue;
            }

            state.footprints.push({ reference: String(reference), points: points });
        }
    }

    // ---- requests --------------------------------------------------------------------------

    async function readJson(response) {
        try {
            return await response.json();
        } catch (error) {
            return null;
        }
    }

    /** One session-relayed read: { status, data } with status 0 for a request that never got an answer. */
    async function requestJson(url, options) {
        let response;
        try {
            response = await digiUser.fetchWithAuth(url, options);
        } catch (error) {
            return { status: 0, data: null };
        }

        const status = response.status;
        const data = response.ok ? await readJson(response) : null;
        return { status: status, data: data };
    }

    async function postJson(path, body) {
        try {
            const response = await digiUser.fetchWithAuth(getResolvedUrl(path), {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            });

            return { status: response.status };
        } catch (error) {
            return { status: 0 };
        }
    }

    function failureMessage(what, status) {
        if (status === 0) {
            return 'The service could not be reached.';
        }

        if (status === 401) {
            return 'Your session has expired. Sign in again.';
        }

        if (status === 400) {
            return 'The request was refused (status 400). Check the bounding box.';
        }

        if (status === 503) {
            return 'The service could not be reached.';
        }

        return `The ${what} could not be read (status ${status}).`;
    }

    // ---- bounding box ----------------------------------------------------------------------

    function bboxQuery(bbox) {
        return 'x_1=' + encodeURIComponent(num(bbox.x1)) +
            '&y_1=' + encodeURIComponent(num(bbox.y1)) +
            '&x_2=' + encodeURIComponent(num(bbox.x2)) +
            '&y_2=' + encodeURIComponent(num(bbox.y2));
    }

    function areaUrl(path, bbox) {
        return getResolvedUrl('/ortodata/area/' + path + '?' + bboxQuery(bbox));
    }

    function imageUrl(year) {
        return areaUrl('image', state.bbox) + '&year=' + encodeURIComponent(String(year));
    }

    /**
     * Reads the four fields, normalizes the corners and enforces the same maximum side the
     * service enforces (the server-side guard is mirrored here so an over-wide area is refused
     * before a request is spent on it). Returns null with the reason on the status line.
     */
    function readBoundingBox() {
        const texts = [elements.x1.value.trim(), elements.y1.value.trim(), elements.x2.value.trim(), elements.y2.value.trim()];
        if (texts.some(function (text) { return text === ''; })) {
            showStatus('Enter all four coordinates of the bounding box (EPSG:2180).');
            return null;
        }

        const values = texts.map(Number);
        if (values.some(function (value) { return !Number.isFinite(value); })) {
            showStatus('The bounding box must hold numbers only.');
            return null;
        }

        const bbox = {
            x1: Math.min(values[0], values[2]),
            y1: Math.min(values[1], values[3]),
            x2: Math.max(values[0], values[2]),
            y2: Math.max(values[1], values[3])
        };

        const width = bbox.x2 - bbox.x1;
        const height = bbox.y2 - bbox.y1;
        if (width <= 0 || height <= 0) {
            showStatus('The bounding box must have a positive side.');
            return null;
        }

        if (Math.max(width, height) > state.maxAreaSide) {
            showStatus(`Each side of the bounding box must be at most ${num(state.maxAreaSide)} m.`);
            return null;
        }

        // The normalized corners go back into the fields, so the URL and the relays always see min <= max.
        elements.x1.value = num(bbox.x1);
        elements.y1.value = num(bbox.y1);
        elements.x2.value = num(bbox.x2);
        elements.y2.value = num(bbox.y2);

        return bbox;
    }

    // ---- identity --------------------------------------------------------------------------

    /**
     * Reads the signed-in e-mail once. The label rows are stamped with it server-side, so it is
     * what tells this reader's own rows (editable) from every other interpreter's (read-only).
     * A failure is remembered as "unreadable" rather than retried per click.
     */
    async function ensureIdentity() {
        if (state.identityState !== 'pending') {
            return;
        }

        state.identityState = 'resolving';
        try {
            const user = await digiUser.getUser();
            const email = user ? String(user.Email || user.email || '').trim() : '';
            state.interpreter = email === '' ? null : email.toLowerCase();
        } catch (error) {
            state.interpreter = null;
        }

        state.identityState = 'resolved';
    }

    function isMe(interpreter) {
        return state.interpreter !== null && String(interpreter || '').trim().toLowerCase() === state.interpreter;
    }

    // ---- labels ----------------------------------------------------------------------------

    /** Footprint rows are keyed by reference alone: the footprint read carries no county id. */
    function identityOf(row) {
        return row.objectId !== null && row.objectId !== undefined && row.objectId !== ''
            ? 'drawn:' + row.objectId
            : 'footprint:' + String(row.reference || '');
    }

    function selectionIdentity(selection) {
        return selection.kind === 'drawn'
            ? 'drawn:' + selection.objectId
            : 'footprint:' + String(selection.reference || '');
    }

    function labelsFor(identity, year) {
        return state.labels.filter(function (row) { return identityOf(row) === identity && row.epochYear === year; });
    }

    function myLabelFor(identity, year) {
        const rows = labelsFor(identity, year);
        for (const row of rows) {
            if (isMe(row.interpreter)) {
                return row;
            }
        }

        return null;
    }

    function foreignRowsFor(identity, year) {
        return labelsFor(identity, year).filter(function (row) { return !isMe(row.interpreter); });
    }

    /** Best effort: a county id only travels in the label body when a row already carries one. */
    function knownCountyId(reference) {
        for (const row of state.labels) {
            if (String(row.reference || '') === String(reference || '') && row.countyId !== null && row.countyId !== undefined) {
                return row.countyId;
            }
        }

        return null;
    }

    // ---- rendering: cards ------------------------------------------------------------------

    function toSvgX(x) {
        return (x - state.bbox.x1) / (state.bbox.x2 - state.bbox.x1) * svgSize;
    }

    function toSvgY(y) {
        return svgSize - (y - state.bbox.y1) / (state.bbox.y2 - state.bbox.y1) * svgSize;
    }

    function svgElement(tag) {
        return document.createElementNS('http://www.w3.org/2000/svg', tag);
    }

    function pointsAttribute(points) {
        return points.map(function (point) { return `${toSvgX(point[0]).toFixed(2)},${toSvgY(point[1]).toFixed(2)}`; }).join(' ');
    }

    function stateClassFor(identity, year) {
        const row = myLabelFor(identity, year);
        if (row === null || !Number.isFinite(row.state)) {
            return '';
        }

        if (row.state === 0) {
            return ' is-present';
        }

        if (row.state === 1) {
            return ' is-absent';
        }

        if (row.state === 2) {
            return ' is-indeterminate';
        }

        return '';
    }

    function selectPolygon(kind, reference, objectId, year) {
        state.selection = {
            kind: kind,
            reference: reference,
            objectId: objectId,
            year: year
        };

        renderCards();
        renderMarking();
    }

    function buildPolygon(className, points, titleText, clickHandler) {
        const polygon = svgElement('polygon');
        polygon.setAttribute('class', className);
        polygon.setAttribute('points', pointsAttribute(points));
        polygon.setAttribute('vector-effect', 'non-scaling-stroke');

        if (titleText !== null && titleText !== undefined && titleText !== '') {
            const title = svgElement('title');
            title.textContent = titleText;
            polygon.appendChild(title);
        }

        if (clickHandler !== null) {
            polygon.addEventListener('click', clickHandler);
        }

        return polygon;
    }

    function buildOverlay(year, card) {
        const svg = svgElement('svg');
        svg.setAttribute('class', 'ortodata-area-overlay');
        svg.setAttribute('viewBox', `0 0 ${svgSize} ${svgSize}`);
        svg.setAttribute('preserveAspectRatio', 'none');

        // Sampled cells first, so the polygons above them stay clickable.
        const selectedCellIndex = selectedObjectCellIndex();
        for (const cell of state.cells) {
            const rect = svgElement('rect');
            rect.setAttribute('class', 'ortodata-area-cell' + (selectedCellIndex !== null && cell.index === selectedCellIndex ? ' is-selected' : ''));
            rect.setAttribute('x', toSvgX(cell.bbox.x1).toFixed(2));
            rect.setAttribute('y', toSvgY(cell.bbox.y2).toFixed(2));
            rect.setAttribute('width', Math.max(0, toSvgX(cell.bbox.x2) - toSvgX(cell.bbox.x1)).toFixed(2));
            rect.setAttribute('height', Math.max(0, toSvgY(cell.bbox.y1) - toSvgY(cell.bbox.y2)).toFixed(2));
            rect.setAttribute('vector-effect', 'non-scaling-stroke');

            const title = svgElement('title');
            title.textContent = `Sampled cell ${cell.index}`;
            rect.appendChild(title);
            svg.appendChild(rect);
        }

        const isSelected = function (identity) {
            return state.selection !== null && selectionIdentity(state.selection) === identity;
        };

        for (const footprint of state.footprints) {
            const identity = 'footprint:' + footprint.reference;
            const className = 'ortodata-area-footprint' + stateClassFor(identity, year) + (isSelected(identity) ? ' selected' : '');
            svg.appendChild(buildPolygon(className, footprint.points, `Footprint ${footprint.reference} - click to mark`, function (event) {
                if (state.drawMode) {
                    return; // the canvas handler below places the vertex; this click bubbles to it
                }

                event.stopPropagation();
                selectPolygon('footprint', footprint.reference, null, year);
            }));
        }

        for (const object of state.objects) {
            const identity = 'drawn:' + object.objectId;
            const cellText = object.cellIndex === null || object.cellIndex === undefined ? 'outside the sampled cells' : `cell ${object.cellIndex}`;
            const className = 'ortodata-area-drawn' + stateClassFor(identity, year) + (isSelected(identity) ? ' selected' : '');
            svg.appendChild(buildPolygon(className, object.points, `Drawn polygon ${shortId(object.objectId)} (${cellText}) - click to mark`, function (event) {
                if (state.drawMode) {
                    return;
                }

                event.stopPropagation();
                selectPolygon('drawn', null, object.objectId, year);
            }));
        }

        if (state.drawMode && state.drawCardYear === year) {
            svg.appendChild(buildDraftGroup());
        }

        card.querySelector('.ortodata-area-canvas').appendChild(svg);
    }

    function buildDraftGroup() {
        const group = svgElement('g');
        group.setAttribute('class', 'ortodata-area-draft');
        if (state.drawVertices.length === 0) {
            return group;
        }

        const polyline = svgElement('polyline');
        polyline.setAttribute('points', pointsAttribute(state.drawVertices));
        polyline.setAttribute('vector-effect', 'non-scaling-stroke');
        group.appendChild(polyline);

        for (const vertex of state.drawVertices) {
            const circle = svgElement('circle');
            circle.setAttribute('class', 'ortodata-area-vertex');
            circle.setAttribute('cx', toSvgX(vertex[0]).toFixed(2));
            circle.setAttribute('cy', toSvgY(vertex[1]).toFixed(2));
            circle.setAttribute('r', '6');
            group.appendChild(circle);
        }

        return group;
    }

    function renderDraft() {
        if (state.drawCardYear === null) {
            return;
        }

        const card = cardForYear(state.drawCardYear);
        if (card === null) {
            return;
        }

        const svg = card.querySelector('.ortodata-area-overlay');
        if (svg === null) {
            return;
        }

        const existing = svg.querySelector('.ortodata-area-draft');
        if (existing !== null) {
            existing.remove();
        }

        svg.appendChild(buildDraftGroup());
    }

    function shortId(objectId) {
        const text = String(objectId || '');
        return text.length > 8 ? text.substring(0, 8) : text;
    }

    function selectedObjectCellIndex() {
        if (state.selection === null || state.selection.kind !== 'drawn') {
            return null;
        }

        const object = state.objects.find(function (candidate) { return candidate.objectId === state.selection.objectId; });
        if (object === undefined || object.cellIndex === null || object.cellIndex === undefined) {
            return null;
        }

        return object.cellIndex;
    }

    function cardForYear(year) {
        return elements.grid.querySelector(`.ortodata-area-card[data-year="${year}"]`);
    }

    function buildCard(year) {
        const card = document.createElement('div');
        card.className = 'ortodata-card ortodata-area-card';
        card.dataset.year = String(year);

        const canvas = document.createElement('div');
        canvas.className = 'ortodata-area-canvas';
        const width = state.bbox.x2 - state.bbox.x1;
        const height = state.bbox.y2 - state.bbox.y1;
        canvas.style.aspectRatio = `${num(width)} / ${num(height)}`;

        const image = document.createElement('img');
        image.loading = 'lazy';
        image.alt = `Orthophoto from ${year}`;

        // A year the negative cache holds no imagery for keeps its card and wears the badge: a missing
        // image is data (the epoch is marked indeterminate), never a reason to hide the card. The
        // handler is wired before the source so an instantly failed load cannot slip past it.
        const badge = document.createElement('span');
        badge.className = 'ortodata-area-badge';
        badge.textContent = 'indeterminate - no imagery';
        badge.hidden = true;

        image.onerror = function () {
            image.hidden = true;
            badge.hidden = false;
        };

        image.src = imageUrl(year);

        const label = document.createElement('span');
        label.className = 'ortodata-year';
        label.textContent = String(year);

        canvas.appendChild(image);
        canvas.appendChild(badge);
        card.appendChild(canvas);
        card.appendChild(label);

        // The overlay attaches itself into the canvas, after the image, so it stacks on top of it.
        buildOverlay(year, card);

        canvas.addEventListener('click', function (event) {
            if (!state.drawMode) {
                return;
            }

            if (state.drawCardYear !== null && state.drawCardYear !== year) {
                return; // one polygon is drawn on one card
            }

            placeVertex(event, canvas, year);
        });

        if (state.selection !== null && state.selection.year === year) {
            card.classList.add('selected');
        }

        return card;
    }

    function renderCards() {
        elements.grid.textContent = '';
        for (const year of state.years) {
            elements.grid.appendChild(buildCard(year));
        }
    }

    // ---- rendering: the marking control ----------------------------------------------------

    function objectLabel(selection) {
        if (selection.kind === 'drawn') {
            const object = state.objects.find(function (candidate) { return candidate.objectId === selection.objectId; });
            const cell = object !== undefined && object.cellIndex !== null && object.cellIndex !== undefined
                ? ` - cell ${object.cellIndex}`
                : ' - outside the sampled cells';
            return `Drawn polygon ${shortId(selection.objectId)}${cell}`;
        }

        return `Footprint ${selection.reference}`;
    }

    function renderMarking() {
        const bar = elements.marking;
        bar.textContent = '';

        if (state.selection === null || state.bbox === null) {
            bar.hidden = true;
            return;
        }

        bar.hidden = false;
        const selection = state.selection;
        const identity = selectionIdentity(selection);

        const line = document.createElement('div');
        line.className = 'ortodata-area-object-line';

        const object = document.createElement('span');
        object.className = 'ortodata-area-object';
        object.textContent = objectLabel(selection);

        const epoch = document.createElement('span');
        epoch.className = 'ortodata-area-epoch';
        epoch.textContent = String(selection.year);

        line.appendChild(object);
        line.appendChild(epoch);
        bar.appendChild(line);

        const states = document.createElement('div');
        states.className = 'ortodata-area-states';

        const myLabel = myLabelFor(identity, selection.year);
        const readOnly = state.interpreter === null;
        for (let code = 0; code < stateNames.length; code++) {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'ortodata-bound';
            button.textContent = stateNames[code];
            button.setAttribute('aria-pressed', myLabel !== null && myLabel.state === code ? 'true' : 'false');
            button.disabled = readOnly || state.posting;
            button.dataset.state = String(code);
            button.addEventListener('click', function () {
                setState(code);
            });
            states.appendChild(button);
        }

        bar.appendChild(states);

        if (readOnly) {
            const hint = document.createElement('span');
            hint.className = 'ortodata-area-foreign-hint';
            hint.textContent = 'The signed-in identity could not be read, so the labels are shown read-only.';
            bar.appendChild(hint);
        }

        // Other interpreters' rows: visible for the agreement, never editable from this session.
        const foreignRows = foreignRowsFor(identity, selection.year);
        for (const row of foreignRows) {
            const foreign = document.createElement('div');
            foreign.className = 'ortodata-area-foreign-label';

            const stateName = Number.isFinite(row.state) && row.state >= 0 && row.state < stateNames.length ? stateNames[row.state] : String(row.state);
            const parts = [stateName, String(row.interpreter || 'unknown interpreter')];
            if (row.updatedAt) {
                parts.push(String(row.updatedAt));
            }

            foreign.textContent = parts.join(' - ');
            bar.appendChild(foreign);
        }
    }

    async function setState(code) {
        if (state.selection === null || state.bbox === null || state.posting) {
            return;
        }

        if (state.interpreter === null) {
            showStatus('The signed-in identity could not be read, so marking is unavailable.');
            return;
        }

        const selection = state.selection;
        const identity = selectionIdentity(selection);
        const current = myLabelFor(identity, selection.year);
        if (current !== null && current.state === code) {
            return; // already the recorded state; the contract keeps one row per identity x epoch x interpreter
        }

        const body = {
            x_1: state.bbox.x1,
            y_1: state.bbox.y1,
            x_2: state.bbox.x2,
            y_2: state.bbox.y2,
            epochYear: selection.year,
            state: code
        };

        if (selection.kind === 'drawn') {
            body.objectId = selection.objectId;
        } else {
            body.reference = selection.reference;
            const countyId = knownCountyId(selection.reference);
            if (countyId !== null) {
                body.countyId = countyId;
            }
        }

        // Optimistic: the row lands on every card at once and is rolled back if the relay refuses.
        const backup = state.labels.slice();
        state.labels = state.labels.filter(function (row) { return !(identityOf(row) === identity && row.epochYear === selection.year && isMe(row.interpreter)); });

        const optimistic = {
            areaKey: state.areaKey,
            countyId: body.countyId,
            reference: selection.kind === 'footprint' ? selection.reference : undefined,
            objectId: selection.kind === 'drawn' ? selection.objectId : undefined,
            epochYear: selection.year,
            state: code,
            interpreter: state.interpreter,
            updatedAt: new Date().toISOString()
        };
        state.labels.push(optimistic);

        state.posting = true;
        renderCards();
        renderMarking();

        const result = await postJson('/ortodata/area/label', body);

        state.posting = false;
        if (result.status < 200 || result.status >= 300) {
            state.labels = backup;
            renderCards();
            renderMarking();

            if (result.status === 0) {
                showStatus('The service could not be reached. The label was not saved.');
            } else if (result.status === 401) {
                showStatus('Your session has expired. Sign in again.');
            } else {
                showStatus(`The label could not be saved (status ${result.status}).`);
            }

            return;
        }

        hideStatus();
        renderMarking();
    }

    // ---- drawing ---------------------------------------------------------------------------

    function setDrawing(drawing) {
        state.drawMode = drawing;
        elements.draw.setAttribute('aria-pressed', drawing ? 'true' : 'false');
        elements.close.hidden = !drawing;
        elements.grid.classList.toggle('drawing', drawing);
    }

    function startDraw() {
        if (state.bbox === null) {
            showStatus('Load an area before drawing a polygon.');
            return;
        }

        setDrawing(true);
        showStatus('Click a card to place the first point. Click that first point again, or Close, to finish - at least three points. Esc cancels.');
    }

    function cancelDraw() {
        state.drawVertices = [];
        state.drawCardYear = null;
        setDrawing(false);
        renderCards();
        hideStatus();
    }

    function worldPoint(event, canvas) {
        const rect = canvas.getBoundingClientRect();
        const ratioX = (event.clientX - rect.left) / rect.width;
        const ratioY = (event.clientY - rect.top) / rect.height;
        return [
            state.bbox.x1 + ratioX * (state.bbox.x2 - state.bbox.x1),
            state.bbox.y2 - ratioY * (state.bbox.y2 - state.bbox.y1)
        ];
    }

    function screenPoint(world, canvas) {
        const rect = canvas.getBoundingClientRect();
        return {
            x: rect.left + (world[0] - state.bbox.x1) / (state.bbox.x2 - state.bbox.x1) * rect.width,
            y: rect.top + (state.bbox.y2 - world[1]) / (state.bbox.y2 - state.bbox.y1) * rect.height
        };
    }

    function placeVertex(event, canvas, year) {
        if (state.drawCardYear === null) {
            state.drawCardYear = year;
        }

        // Closing on the first point is judged in screen pixels, so the tolerance means the same
        // whatever the zoom of the card.
        if (state.drawVertices.length >= 3) {
            const first = screenPoint(state.drawVertices[0], canvas);
            const distance = Math.hypot(event.clientX - first.x, event.clientY - first.y);
            if (distance <= closeTolerancePx) {
                finishDraw();
                return;
            }
        }

        state.drawVertices.push(worldPoint(event, canvas));
        renderDraft();
    }

    async function finishDraw() {
        if (state.posting) {
            return; // one submission at a time: a double click on Close must not create two objects
        }

        if (state.drawVertices.length < 3) {
            showStatus('A polygon needs at least three points.');
            return;
        }

        const year = state.drawCardYear;
        const body = {
            x_1: state.bbox.x1,
            y_1: state.bbox.y1,
            x_2: state.bbox.x2,
            y_2: state.bbox.y2,
            polygon: state.drawVertices
        };

        state.posting = true;
        const result = await postJson('/ortodata/area/object', body);
        state.posting = false;
        if (result.status < 200 || result.status >= 300) {
            // The vertices stay put so the same polygon can be submitted again.
            if (result.status === 0) {
                showStatus('The service could not be reached. The polygon was not saved.');
            } else if (result.status === 401) {
                showStatus('Your session has expired. Sign in again.');
            } else {
                showStatus(`The polygon could not be saved (status ${result.status}).`);
            }

            return;
        }

        const knownIds = state.objects.map(function (object) { return object.objectId; });

        state.drawVertices = [];
        state.drawCardYear = null;
        setDrawing(false);
        hideStatus();

        // The created object (its server-assigned id and sampled-cell index) is re-read from the
        // area items, then selected so its cell shows on the control at once.
        const itemsResult = await requestJson(areaUrl('items', state.bbox), { method: 'GET' });
        if (itemsResult.status === 200 && itemsResult.data !== null && applyAreaItems(itemsResult.data)) {
            const created = state.objects.find(function (object) { return knownIds.indexOf(object.objectId) === -1; });
            if (created !== undefined && year !== null) {
                state.selection = { kind: 'drawn', reference: null, objectId: created.objectId, year: year };
            }
        } else {
            showStatus(failureMessage('area items', itemsResult.status));
        }

        renderCards();
        renderMarking();
    }

    // ---- load ------------------------------------------------------------------------------

    async function loadArea() {
        if (state.busy) {
            return;
        }

        const bbox = readBoundingBox();
        if (bbox === null) {
            return;
        }

        state.busy = true;
        elements.load.disabled = true;
        try {
            showStatus('Loading the area...');

            // The address carries the contract: mode=area plus the normalized box, so a refresh or
            // a toggle away and back lands on the same area.
            history.replaceState(null, '', getResolvedUrl('/ortodata?mode=area&' + bboxQuery(bbox)));

            const [yearsResult, itemsResult, labelsResult, buildingsResult] = await Promise.all([
                requestJson(areaUrl('availableyears', bbox), { method: 'GET' }),
                requestJson(areaUrl('items', bbox), { method: 'GET' }),
                requestJson(areaUrl('labels', bbox), { method: 'GET' }),
                requestJson(areaUrl('buildings', bbox), { method: 'GET' })
            ]);

            if (yearsResult.status !== 200 || !Array.isArray(yearsResult.data)) {
                showStatus(failureMessage('epoch years', yearsResult.status));
                return;
            }

            if (itemsResult.status !== 200 || itemsResult.data === null) {
                showStatus(failureMessage('area items', itemsResult.status));
                return;
            }

            if (labelsResult.status !== 200 || labelsResult.data === null) {
                showStatus(failureMessage('labels', labelsResult.status));
                return;
            }

            // The footprint read answers 404 when no building lies in the box - an empty overlay,
            // not a failure; every other non-200 aborts the load.
            if (buildingsResult.status !== 200 && buildingsResult.status !== 404) {
                showStatus(failureMessage('footprints', buildingsResult.status));
                return;
            }

            if (!applyAreaItems(itemsResult.data)) {
                showStatus('The area read could not be understood.');
                return;
            }

            state.bbox = bbox;
            state.years = yearsResult.data.map(Number).filter(function (year) { return Number.isFinite(year); });
            state.years.sort(function (left, right) { return left - right; });
            state.labels = asArray(labelsResult.data).map(normaliseLabelRow);
            applyFootprints(buildingsResult.data);
            state.selection = null;

            if (state.drawMode) {
                // The vertices belonged to the previous box.
                state.drawVertices = [];
                state.drawCardYear = null;
                setDrawing(false);
            }

            renderCards();
            renderMarking();

            elements.draw.disabled = false;
            elements.export.disabled = false;

            if (state.years.length === 0) {
                showStatus('No imagery epochs cover this bounding box.');
            } else {
                hideStatus();
            }
        } finally {
            state.busy = false;
            elements.load.disabled = false;
        }
    }

    // ---- export ----------------------------------------------------------------------------

    /** RFC 4180 cell: quotes anything holding a comma, a quote or a line break, doubling the quotes. */
    function csvCell(value) {
        const text = value === null || value === undefined ? '' : String(value);
        if (/[",\r\n]/.test(text)) {
            return '"' + text.replace(/"/g, '""') + '"';
        }

        return text;
    }

    /** POLYGON WKT with a closed outer ring; the inner edges of a footprint outline are not carried. */
    function polygonWkt(points) {
        if (!Array.isArray(points) || points.length < 3) {
            return '';
        }

        const coordinates = [];
        for (const point of points) {
            coordinates.push(`${num(point[0])} ${num(point[1])}`);
        }

        coordinates.push(`${num(points[0][0])} ${num(points[0][1])}`);
        return 'POLYGON((' + coordinates.join(',') + '))';
    }

    function wktFor(row) {
        if (row.objectId !== null && row.objectId !== undefined && row.objectId !== '') {
            const object = state.objects.find(function (candidate) { return String(candidate.objectId) === String(row.objectId); });
            return object === undefined ? '' : polygonWkt(object.points);
        }

        const footprint = state.footprints.find(function (candidate) { return String(candidate.reference) === String(row.reference || ''); });
        return footprint === undefined ? '' : polygonWkt(footprint.points);
    }

    function cellIdFor(row) {
        if (row.objectId === null || row.objectId === undefined || row.objectId === '') {
            return ''; // footprints are never cell-bound
        }

        const object = state.objects.find(function (candidate) { return String(candidate.objectId) === String(row.objectId); });
        if (object === undefined || object.cellIndex === null || object.cellIndex === undefined) {
            return '';
        }

        return num(Number(object.cellIndex));
    }

    function csvRow(values) {
        return values.map(csvCell).join(',');
    }

    /**
     * Assembles the fixed-column export: one row per label row, every interpreter's rows included,
     * every number and date written invariantly (the columns are the tracking issue's contract).
     */
    function buildCsv(rows) {
        const header = 'area_key,bbox,cell_id,object_kind,county_id,reference,object_id,polygon_wkt,epoch_year,observation_date_min,observation_date_max,state,interpreter,note,updated_at';

        let areaKey = state.areaKey;
        if (areaKey === '' && rows.length > 0 && rows[0].areaKey) {
            areaKey = rows[0].areaKey;
        }

        const bbox = `${num(state.bbox.x1)},${num(state.bbox.y1)},${num(state.bbox.x2)},${num(state.bbox.y2)}`;

        const lines = [header];
        for (const row of rows) {
            const isDrawn = row.objectId !== null && row.objectId !== undefined && row.objectId !== '';
            lines.push(csvRow([
                areaKey,
                bbox,
                cellIdFor(row),
                isDrawn ? 'drawn' : 'footprint',
                row.countyId === null || row.countyId === undefined ? '' : num(Number(row.countyId)),
                row.reference === null || row.reference === undefined ? '' : row.reference,
                isDrawn ? String(row.objectId) : '',
                wktFor(row),
                num(row.epochYear),
                row.observationDateMin === null || row.observationDateMin === undefined ? '' : row.observationDateMin,
                row.observationDateMax === null || row.observationDateMax === undefined ? '' : row.observationDateMax,
                num(row.state),
                row.interpreter === null || row.interpreter === undefined ? '' : row.interpreter,
                row.note === null || row.note === undefined ? '' : row.note,
                row.updatedAt === null || row.updatedAt === undefined ? '' : row.updatedAt
            ]));
        }

        return lines.join('\r\n') + '\r\n';
    }

    /** downloadText idiom of typology.js: a Blob, a temporary anchor, one click. */
    function downloadText(text, fileName) {
        const blob = new Blob([text], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = fileName;
        document.body.appendChild(anchor);
        anchor.click();
        document.body.removeChild(anchor);
        setTimeout(function () { URL.revokeObjectURL(url); }, 0);
    }

    async function exportCsv() {
        if (state.busy) {
            return;
        }

        if (state.bbox === null) {
            showStatus('Load an area before exporting.');
            return;
        }

        state.busy = true;
        elements.export.disabled = true;
        try {
            showStatus('Reading the labels...');

            // The label read is repeated rather than reused: the export must carry every interpreter's
            // rows as they stand on the server, not as this session last saw them.
            const labelsResult = await requestJson(areaUrl('labels', state.bbox), { method: 'GET' });
            if (labelsResult.status !== 200 || labelsResult.data === null) {
                showStatus(failureMessage('labels', labelsResult.status));
                return;
            }

            const rows = asArray(labelsResult.data).map(normaliseLabelRow);
            if (rows.length === 0) {
                showStatus('There are no label rows to export yet.');
                return;
            }

            const areaKeyText = (state.areaKey || (rows[0].areaKey ? String(rows[0].areaKey) : 'area'))
                .replace(/[^\w.-]+/g, '_');
            downloadText(buildCsv(rows), `orto-area-${areaKeyText}.csv`);
            hideStatus();
        } finally {
            state.busy = false;
            elements.export.disabled = false;
        }
    }

    // ---- wiring ----------------------------------------------------------------------------

    if (elements.panel === null || elements.load === null || elements.grid === null) {
        // The area markup is only rendered for mode=area; nothing to drive otherwise.
        return;
    }

    state.maxAreaSide = Number(elements.panel.dataset.maxAreaSide);
    if (!Number.isFinite(state.maxAreaSide) || state.maxAreaSide <= 0) {
        state.maxAreaSide = defaultMaxAreaSide;
    }

    elements.load.addEventListener('click', function () {
        loadArea();
    });

    elements.draw.addEventListener('click', function () {
        if (state.drawMode) {
            cancelDraw();
        } else {
            startDraw();
        }
    });

    elements.close.addEventListener('click', function () {
        finishDraw();
    });

    elements.export.addEventListener('click', function () {
        exportCsv();
    });

    document.addEventListener('keydown', function (event) {
        if (!state.drawMode || event.key !== 'Escape') {
            return;
        }

        const tag = event.target && event.target.tagName ? event.target.tagName.toUpperCase() : '';
        if (tag === 'INPUT' || tag === 'TEXTAREA') {
            return;
        }

        cancelDraw();
    });

    (async function start() {
        await ensureIdentity();

        const query = new URLSearchParams(window.location.search);
        const coordinates = ['x_1', 'y_1', 'x_2', 'y_2'].map(function (key) { return query.get(key); });
        if (coordinates.every(function (value) { return value !== null && value !== ''; })) {
            elements.x1.value = coordinates[0];
            elements.y1.value = coordinates[1];
            elements.x2.value = coordinates[2];
            elements.y2.value = coordinates[3];
            await loadArea();
            return;
        }

        showStatus(`Enter a bounding box (EPSG:2180, each side at most ${num(state.maxAreaSide)} m) and select Load.`);
    })();
})();
