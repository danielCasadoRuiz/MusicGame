const fs = require('node:fs');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { createInterface } = require('node:readline/promises');
const { createRequire } = require('node:module');
const { homedir } = require('node:os');

const root = path.resolve(__dirname, '../../..');
const project = path.join(root, 'MusicGame');
const catalogPath = path.join(project, 'Assets/_Project/Configs/Song/PlayableSongCatalog.asset');

function catalogSongs() {
    const yaml = fs.readFileSync(catalogPath, 'utf8');
    const musicFolder = path.join(project, 'Assets/_Project/Audio/Music');
    const filenames = new Map();
    for (const entry of fs.readdirSync(musicFolder, { recursive: true, withFileTypes: true })) {
        if (!entry.isFile() || !entry.name.endsWith('.meta')) continue;
        const metaPath = path.join(entry.parentPath, entry.name);
        const guid = fs.readFileSync(metaPath, 'utf8').match(/^guid: ([0-9a-f]{32})$/m)?.[1];
        if (guid && fs.existsSync(metaPath.slice(0, -5))) filenames.set(guid, entry.name.slice(0, -5));
    }
    // Resolve each source filename by the GUID Unity stores in the playable catalog.
    return [...yaml.matchAll(/^  - songId: (song_[a-zA-Z0-9_]+)\r?\n    address: (.+)\r?\n    editorGuid: ([0-9a-f]{32})$/gm)]
        .map(([, songId, address, guid]) => ({
            filename: filenames.get(guid),
            songId,
            name: address.startsWith('"') ? JSON.parse(address.replace(/\\x([0-9a-f]{2})/gi, '\\u00$1')) : address
        }));
}

async function fetchSong(url, id) {
    const localRequire = createRequire(path.join(homedir(), 'package.json'));
    const { chromium } = localRequire('playwright');
    const browser = await chromium.connectOverCDP('http://127.0.0.1:9222', { timeout: 5000 })
        .catch(() => { throw new Error('Obre el Chrome de la sessió de Suno (port 9222) i torna-ho a provar.'); });
    try {
        const context = browser.contexts()[0];
        const page = await context.newPage();
        try {
            await page.goto(url, { waitUntil: 'domcontentloaded' });
            return await page.evaluate(async songId => {
                if (location.origin !== 'https://suno.com') throw new Error('Suno ha redirigit la pàgina.');
                const cookie = document.cookie.match(/(?:^|;\s*)__session=([^;]+)/);
                if (!cookie) throw new Error('Inicia sessió a Suno en aquest Chrome.');
                const headers = { Authorization: 'Bearer ' + decodeURIComponent(cookie[1]) };
                async function get(endpoint) {
                    const response = await fetch('https://studio-api.prod.suno.com/api/' + endpoint,
                        { headers, credentials: 'omit', cache: 'no-store' });
                    if (!response.ok) throw new Error('Suno ha retornat HTTP ' + response.status);
                    return response.json();
                }
                const [alignment, clip] = await Promise.all([
                    get('gen/' + songId + '/aligned_lyrics/v2/'), get('clip/' + songId)
                ]);
                return { rows: alignment.aligned_lyrics ?? alignment.data?.aligned_lyrics,
                    title: clip.title, duration: clip.metadata?.duration };
            }, id);
        } finally { await page.close(); }
    } finally { await browser.close(); }
}

function cues(rows) {
    const lines = [];
    for (const row of rows ?? []) {
        const words = (row.words ?? []).filter(word => Number.isFinite(word.start_s ?? word.start)
            && Number.isFinite(word.end_s ?? word.end));
        const text = String(row.text ?? row.word ?? words.map(word => word.text ?? word.word).join(' ')).trim();
        const start = words.length ? Math.min(...words.map(word => word.start_s ?? word.start)) : row.start_s ?? row.start;
        const end = words.length ? Math.max(...words.map(word => word.end_s ?? word.end)) : row.end_s ?? row.end;
        if (!text || /^\[[^\]]+\]$/.test(text) || !Number.isFinite(start) || !Number.isFinite(end) || start < 0 || end <= start) continue;
        const previous = lines.at(-1);
        if (previous && start < previous.end) {
            if (start <= previous.start) throw new Error('Suno ha retornat fragments amb temps desordenats.');
            previous.end = start;
        }
        lines.push({ start, end, text });
    }
    if (!lines.length) throw new Error('Suno no ha retornat cap fragment amb temps.');
    return lines;
}

async function main() {
    const prompt = createInterface({ input: process.stdin, output: process.stdout });
    let stage, log, editor;
    try {
        const input = (await prompt.question('Enllaç de la cançó de Suno: ')).trim();
        const url = new URL(input);
        const id = url.pathname.match(/^\/song\/([0-9a-f-]{36})\/?$/i)?.[1];
        if (url.origin !== 'https://suno.com' || !id)
            throw new Error('Enganxa un enllaç https://suno.com/song/…');
        const inventory = catalogSongs();
        if (!inventory.length) throw new Error('No he trobat cap cançó al catàleg de Unity.');
        for (const song of inventory) {
            console.log(song.songId + '  ' + song.name + '  |  ' + (song.filename ?? 'fitxer no localitzat'));
        }
        const songId = (await prompt.question('Identificador de la cançó del joc: ')).trim();
        const song = inventory.find(entry => entry.songId === songId);
        if (!song) throw new Error('La cançó no és a l’inventari.');
        const version = fs.readFileSync(path.join(project, 'ProjectSettings/ProjectVersion.txt'), 'utf8')
            .match(/^m_EditorVersion:\s*(\S+)/m)?.[1];
        editor = process.env.MUSICGAME_UNITY || path.join(process.env.ProgramFiles, 'Unity/Hub/Editor', version, 'Editor/Unity.exe');
        if (!fs.existsSync(editor)) throw new Error('No trobo Unity. Indica la ruta a Unity.exe amb MUSICGAME_UNITY.');

        const result = await fetchSong('https://suno.com/song/' + id, id);
        const lines = cues(result.rows);
        console.log(result.title + ' → ' + song.name);
        console.log(lines.length + ' fragments. Últim fragment a ' + lines.at(-1).end + ' s. Durada de Suno: ' + (result.duration ?? '?') + ' s.');
        if (!/^s$/i.test((await prompt.question('Importar i substituir la lletra anterior si existeix? [s/N] ')).trim())) return;
        const directory = path.join(root, 'AssetsMaterial/Lyrics', songId);
        fs.mkdirSync(directory, { recursive: true });
        stage = path.join(directory, 'suno.lyrics.json');
        log = path.join(directory, 'suno-unity-import.log');
        fs.writeFileSync(stage, JSON.stringify({ schemaVersion: 1, songId, textSource: 'suno_aligned_transcription',
            timingSource: 'suno', offset: 0, lines }, null, 2) + '\n');
    } finally { prompt.close(); }

    console.log('Unity està registrant la lletra...');
    await new Promise((resolve, reject) => {
        const child = spawn(editor, ['-batchmode', '-nographics', '-projectPath', project, '-executeMethod',
            'RemoteSongContentSetup.ImportLyricsFromCommandLine', '-lyricsJson', stage, '-logFile', log],
        { windowsHide: true, stdio: 'ignore' });
        child.once('error', reject);
        child.once('close', code => code === 0 ? resolve() : reject(new Error('Unity no ha completat la importació. Registre: ' + log)));
    });
    console.log('Lletra registrada. S’inclourà a la propera build de contingut.');
}

main().catch(error => { console.error(error.message); process.exitCode = 1; });
