const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

const frames = [];
const events = {};
let receive;
const editor = {
  value: '', style: {}, clientHeight: 200, scrollHeight: 2000,
  scrollTop: 0, scrollLeft: 0,
  addEventListener(name, handler) { events[name] = handler; },
  removeAttribute() {}, focus() {},
  setSelectionRange(start, end) { this.selectionStart = start; this.selectionEnd = end; }
};
const overlay = { style: {} };
const source = fs.readFileSync(require.resolve('../Web/index.html'), 'utf8')
  .match(/<script>([\s\S]*?)<\/script>/)[1];
vm.runInNewContext(source, {
  document: { getElementById: id => id === 'editor' ? editor : overlay },
  window: {
    chrome: { webview: { postMessage() {}, addEventListener(name, handler) { receive = handler; } } },
    addEventListener(name, handler) { events[name] = handler; }
  },
  requestAnimationFrame(handler) { frames.push(handler); return frames.length; },
  Calc: { evaluate: () => [] }
});

const text = '1+1\n'.repeat(100);
receive({ data: 'I' + text });
assert.equal(editor.selectionStart, text.length);
// Simulate WebView restoring the viewport after the initial host message.
editor.scrollTop = 0;
frames.shift()();
assert.equal(editor.scrollTop, editor.scrollHeight);
assert.equal(overlay.style.transform, 'translate(0px,-2000px)');

// Initialization while hidden must survive until the first visible layout.
editor.clientHeight = 0;
receive({ data: 'I' + text });
editor.scrollTop = 0;
frames.shift()();
editor.clientHeight = 200;
events.resize();
assert.equal(editor.scrollTop, editor.scrollHeight);
editor.scrollTop = 100;
events.resize();
assert.equal(editor.scrollTop, 100);

// Typing before the deferred restoration preserves the user's new position.
receive({ data: 'I' + text });
editor.scrollTop = 100;
events.input();
frames.shift()();
assert.equal(editor.scrollTop, 100);
console.log('Startup scroll tests passed');
