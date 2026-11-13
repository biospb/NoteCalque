const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

let receive;
const root = { style: { setProperty() {} }, removeAttribute() {}, setAttribute() {} };
const editor = {
  value: 'sqrt(-bad)', style: {}, scrollTop: 0, scrollLeft: 0,
  addEventListener() {}, removeAttribute() {}, focus() {}
};
const overlay = { style: {}, innerHTML: '' };
const source = fs.readFileSync(require.resolve('../Web/index.html'), 'utf8')
  .match(/<script>([\s\S]*?)<\/script>/)[1];
vm.runInNewContext(source, {
  document: { documentElement: root, getElementById: id => id === 'editor' ? editor : overlay },
  window: {
    chrome: { webview: { postMessage() {}, addEventListener(name, handler) { receive = handler; } } },
    addEventListener() {}
  },
  Calc: { evaluate: () => [{ text: 'Undefined symbol bad', error: true }], setPrecision() {} }
});
for (const [code, label, expectedLanguage] of [
  ['ru', 'Ошибка', 'ru'], ['en', 'Error', 'en'], ['de', 'Error', 'en'], ['', 'Error', 'en']
]) {
  receive({ data: 'Csystem|Consolas|12|12|0|0|' + code });
  assert.equal(root.lang, expectedLanguage);
  assert.ok(overlay.innerHTML.includes('>' + '  ' + label + '</span>'));
  assert.ok(overlay.innerHTML.includes('title="Undefined symbol bad"'));
  assert.equal(editor.value, 'sqrt(-bad)');
}
console.log('Editor language tests passed');
