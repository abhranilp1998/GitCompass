'use strict';
(() => {
  const terminal = new Terminal({
    cursorBlink: true, scrollback: 5000, fontSize: 13,
    fontFamily: 'Cascadia Mono, Consolas, monospace', allowProposedApi: false,
    screenReaderMode: true, convertEol: false,
    theme: { background: '#111827', foreground: '#e5e7eb', cursor: '#60a5fa', selectionBackground: '#334155' },
    linkHandler: { activate() {}, hover() {}, leave() {} }
  });
  const fit = new FitAddon.FitAddon();
  terminal.loadAddon(fit);
  // Explicitly consume OSC clipboard sequences. Terminal data is never HTML and
  // no link addon is loaded. Output cannot open a URL or alter the clipboard.
  terminal.parser.registerOscHandler(52, () => true);
  terminal.open(document.getElementById('terminal'));
  const send = value => window.chrome.webview.postMessage(value);
  terminal.onData(data => send({ type: 'input', data }));
  terminal.onResize(size => send({ type: 'resize', columns: size.cols, rows: size.rows }));
  document.addEventListener('paste', event => {
    event.preventDefault(); event.stopImmediatePropagation();
    if (event.isTrusted) send({ type: 'pasteRequest' });
  }, true);
  terminal.attachCustomKeyEventHandler(event => {
    if (event.type === 'keydown' && event.ctrlKey && event.shiftKey && event.code === 'KeyV') {
      if (event.isTrusted) send({ type: 'pasteRequest' });
      return false;
    }
    // Copy is an explicit WPF dock action. The WebView never writes clipboard.
    return !(event.ctrlKey && event.shiftKey && event.code === 'KeyC');
  });
  document.addEventListener('contextmenu', event => event.preventDefault());
  window.chrome.webview.addEventListener('message', event => {
    const message = event.data;
    if (!message || typeof message.type !== 'string') return;
    if (message.type === 'output' && typeof message.data === 'string') terminal.write(message.data);
    else if (message.type === 'focus') terminal.focus();
  });
  // Used only by an explicit native Copy selection button; never accepts output as script.
  window.gitCompassSelection = () => terminal.getSelection();
  window.gitCompassReadScreen = () => {
    const buffer = terminal.buffer.active;
    const lines = [];
    for (let row = 0; row < buffer.length; row++) lines.push(buffer.getLine(row).translateToString(true));
    return lines.join('\n');
  };
  let resizeFrame;
  new ResizeObserver(() => {
    cancelAnimationFrame(resizeFrame);
    resizeFrame = requestAnimationFrame(() => { try { fit.fit(); } catch {} });
  }).observe(document.body);
  fit.fit(); terminal.focus(); send({ type: 'ready' });
})();
