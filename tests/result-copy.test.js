const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

const events = {};
const messages = [];
const editor = {
  value: '2+2\nbad', style: {}, scrollLeft: 20, scrollTop: 22,
  addEventListener(name, handler) { events[name] = handler; },
  removeAttribute() {}, focus() {}
};
const results = [false, true].map((error, index) => ({
  dataset: { row: String(index) }, title: error ? 'Invalid expression' : '',
  classList: { contains: () => error },
  getBoundingClientRect: () => ({ left: 50, right: 100, top: index * 22, bottom: index * 22 + 20 })
}));
const overlay = { style: {}, querySelectorAll: () => results };
const source = fs.readFileSync(require.resolve('../Web/index.html'), 'utf8').match(/<script>([\s\S]*?)<\/script>/)[1];
vm.runInNewContext(source, {
  document: { getElementById: id => id === 'editor' ? editor : overlay },
  window: { chrome: { webview: { postMessage: value => messages.push(value), addEventListener() {} } }, addEventListener() {} },
  Calc: { evaluate: () => [{ text: '4', error: false }, { text: 'Invalid expression', error: true }] }
});
messages.length = 0;
function click(name, x, y, button = 0) {
  let prevented = false;
  events[name]({ clientX: x, clientY: y, button, preventDefault() { prevented = true; } });
  return prevented;
}
assert.equal(click('mousedown', 70, 10), true);
assert.equal(click('dblclick', 70, 10), true);
assert.deepEqual(messages, ['K4']);
assert.equal(click('dblclick', 30, 10), false); // expression
assert.equal(click('dblclick', 70, 30), false); // error
assert.equal(click('dblclick', 70, 10, 2), false); // right button
assert.equal(click('mousedown', 30, 10), false);
assert.deepEqual(messages, ['K4']);
events.mousemove({ clientX: 70, clientY: 30 });
assert.equal(editor.title, 'Invalid expression');
assert.equal(overlay.style.transform, 'translate(-20px,-22px)');
console.log('Result copy tests passed');
