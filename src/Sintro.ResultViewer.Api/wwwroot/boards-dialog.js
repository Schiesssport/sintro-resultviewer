// DOM layer of the fullscreen picker: boards (a mode plus its settings) are remembered per browser so a
// display's configuration can be looked up again. Seeded with one board per mode; the operator edits, adds and removes.

import { escapeHtml } from './core/format.js';
import { FULLSCREEN_MODES } from './core/viewmode.js';
import { MAX_RESULT_COUNT } from './core/display.js';
import { BOARDS_STORAGE_KEY, parseBoards, normaliseBoard, boardPath } from './core/boards.js';

const el = (id) => document.getElementById(id);

export const createBoardsDialog = ({ t, onShow }) => {
    let boards = [];

    const loadBoards = () => {
        try {
            boards = parseBoards(localStorage.getItem(BOARDS_STORAGE_KEY));
        } catch {
            boards = parseBoards(null);
        }
    };

    const saveBoards = () => {
        try {
            localStorage.setItem(BOARDS_STORAGE_KEY, JSON.stringify(boards));
        } catch {
            // Private mode or blocked storage: the list still works for this page view.
        }
    };

    const boardName = (board) => board.name || t(`fullscreen.mode.${board.mode}`);
    const boardUrl = (board) => new URL(boardPath(board), location.origin).href;

    const numberField = (index, key, label, value, min) => `
            <label for="board-${index}-${key}">${escapeHtml(label)}</label>
            <input type="number" id="board-${index}-${key}" data-index="${index}" data-key="${key}" value="${value}" min="${min}" max="${MAX_RESULT_COUNT}" step="1">`;

    const modeOptions = (selected) => FULLSCREEN_MODES.map((mode) =>
        `<option value="${mode}" ${mode === selected ? 'selected' : ''}>${escapeHtml(t(`fullscreen.mode.${mode}`))}</option>`).join('');

    const boardSettings = (board, index) => `
            <details class="picker-settings-details">
                <summary>${escapeHtml(t('fullscreen.settings'))}</summary>
                <div class="picker-settings">
                    <label for="board-${index}-name">${escapeHtml(t('fullscreen.boardName'))}</label>
                    <input type="text" id="board-${index}-name" data-index="${index}" data-key="name" value="${escapeHtml(board.name)}" placeholder="${escapeHtml(t(`fullscreen.mode.${board.mode}`))}">
                    <label for="board-${index}-mode">${escapeHtml(t('fullscreen.boardMode'))}</label>
                    <select id="board-${index}-mode" data-index="${index}" data-key="mode">${modeOptions(board.mode)}</select>
                    ${numberField(index, 'results', t('fullscreen.resultCount'), board.results, 1)}
                    ${numberField(index, 'skip', t('fullscreen.tickerSkip'), board.skip, 0)}
                    ${numberField(index, 'seconds', t('fullscreen.tickerSeconds'), board.seconds, 1)}
                    <label for="board-${index}-hidden">${escapeHtml(t('fullscreen.hideTicker'))}</label>
                    <input type="checkbox" id="board-${index}-hidden" data-index="${index}" data-key="hidden" ${board.hidden ? 'checked' : ''}>
                    <span></span>
                    <button type="button" class="btn-secondary btn-small" data-remove-board="${index}">${escapeHtml(t('fullscreen.removeBoard'))}</button>
                </div>
            </details>`;

    // A wall display is usually another screen, so every board also offers its URL for pasting.
    const renderBoards = () => {
        el('fullscreen-modes').innerHTML = boards.map((board, index) => `
            <div class="picker-item">
                <div class="picker-row">
                    <div class="picker-text">
                        <div class="picker-name" data-name="${index}">${escapeHtml(boardName(board))}</div>
                        <div class="picker-url" data-url="${index}">${escapeHtml(boardUrl(board))}</div>
                    </div>
                    <button class="btn-action" data-open-board="${index}">${escapeHtml(t('fullscreen.show'))}</button>
                    <button class="btn-secondary" data-copy-board="${index}">${escapeHtml(t('fullscreen.copy'))}</button>
                </div>
                ${boardSettings(board, index)}
            </div>`).join('') + `
            <button type="button" class="btn-secondary" id="add-board">${escapeHtml(t('fullscreen.addBoard'))}</button>`;
    };

    // Reads one board's fields, stores them and refreshes only its name and URL, so typing is not interrupted.
    const onBoardInput = (event) => {
        const index = Number(event.target.dataset.index);
        if (!Number.isInteger(index) || !boards[index]) return;

        const field = (key) => el('fullscreen-modes').querySelector(`[data-index="${index}"][data-key="${key}"]`);
        boards[index] = normaliseBoard({
            name: field('name').value,
            mode: field('mode').value,
            results: field('results').value,
            skip: field('skip').value,
            seconds: field('seconds').value,
            hidden: field('hidden').checked,
        });
        saveBoards();
        el('fullscreen-modes').querySelector(`[data-name="${index}"]`).textContent = boardName(boards[index]);
        el('fullscreen-modes').querySelector(`[data-url="${index}"]`).textContent = boardUrl(boards[index]);
    };

    const onBoardsClick = (event) => {
        const open = event.target.closest('[data-open-board]');
        if (open) return void show(boards[open.dataset.openBoard]);

        const copy = event.target.closest('[data-copy-board]');
        if (copy) return void copyBoardUrl(boards[copy.dataset.copyBoard], copy);

        const remove = event.target.closest('[data-remove-board]');
        if (remove) {
            boards.splice(Number(remove.dataset.removeBoard), 1);
            if (boards.length === 0) boards = parseBoards(null);
            saveBoards();
            return void renderBoards();
        }

        if (event.target.closest('#add-board')) {
            boards.push(normaliseBoard({}));
            saveBoards();
            renderBoards();
            el('fullscreen-modes').querySelector(`.picker-item:last-of-type details`).open = true;
        }
    };

    const copyBoardUrl = async (board, button) => {
        try {
            await navigator.clipboard.writeText(boardUrl(board));
        } catch {
            // Clipboard needs a secure context; on plain http the URL beside the button is still selectable.
            button.textContent = t('fullscreen.copyFailed');
            return;
        }

        button.textContent = t('fullscreen.copied');
        setTimeout(() => { button.textContent = t('fullscreen.copy'); }, 2000);
    };

    const open = () => {
        loadBoards();
        renderBoards();
        el('fullscreen-dialog').showModal();
    };

    const show = (board) => {
        el('fullscreen-dialog').close();
        onShow(board);
    };

    el('fullscreen-modes').addEventListener('input', onBoardInput);
    el('fullscreen-modes').addEventListener('click', onBoardsClick);
    el('fullscreen-dialog-close').addEventListener('click', () => el('fullscreen-dialog').close());

    return { open };
};
