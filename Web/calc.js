// Вычисление блокнота построчно.
//
// Вынесено из index.html отдельным файлом ради тестов: файл собран как UMD,
// поэтому тот же код гоняется в node (tests/calc.test.js) и в WebView2.
//
// Разбором выражений занимается math.js, здесь только две вещи, которых в нём
// нет: проценты в человеческой записи и решение, что делать со строкой,
// которая выражением не является.

(function (root, factory) {
  if (typeof module === 'object' && module.exports) {
    module.exports = factory(require('./math.js'));
  } else {
    root.Calc = factory(root.math);
  }
})(typeof self !== 'undefined' ? self : this, function (math) {
  'use strict';

  // Кириллица в именах переменных. math.js по умолчанию считает буквами только
  // латиницу, а блокнот на русском без «цена = 1200» бессмысленен. Набор
  // допустимых символов у парсера вынесен наружу именно для таких случаев.
  if (math && math.parse && typeof math.parse.isAlpha === 'function' && !math.parse.__cyrillic) {
    var isAlphaBase = math.parse.isAlpha;
    math.parse.isAlpha = function (c, cPrev, cNext) {
      return isAlphaBase(c, cPrev, cNext) || /[Ѐ-ӿԀ-ԯ]/.test(c);
    };
    math.parse.__cyrillic = true;
  }

  // --- проценты ------------------------------------------------------------
  //
  // math.js трактует % как остаток от деления и других значений не знает.
  // Знак один, смыслов два, и различает их только то, что стоит справа:
  // после остатка обязан идти операнд, после процента — конец строки,
  // оператор, закрывающая скобка или слово of.

  var PERCENT_OF = /(^|[^\w.])(\d+(?:\.\d+)?)\s*%\s+of\s+/i;
  var PERCENT_TAIL = /^(.*\S)\s*([+-])\s*(\d+(?:\.\d+)?)\s*%\s*$/;

  // Остаток от деления: за знаком идёт операнд.
  function isModulo(src, at) {
    var rest = src.slice(at + 1).replace(/^\s+/, '');
    if (rest === '') return false;
    if (/^of\b/i.test(rest)) return false;
    return /^[\w.(]/.test(rest);
  }

  function expandPercents(src) {
    // «20% of 250» — доля от величины.
    var out = src;
    while (PERCENT_OF.test(out)) {
      out = out.replace(PERCENT_OF, function (m, lead, num) {
        return lead + '(' + num + ' / 100) * ';
      });
    }

    // «1200 + 15%» — прибавка к тому, что слева, а не 0.15.
    var tail = PERCENT_TAIL.exec(out);
    if (tail && !/%\s*$/.test(tail[1])) {
      var sign = tail[2] === '+' ? '+' : '-';
      return '(' + tail[1] + ') * (1 ' + sign + ' ' + tail[3] + ' / 100)';
    }

    // Всё остальное: одиночный процент — просто сотая доля.
    var res = '';
    var i = 0;
    while (i < out.length) {
      var ch = out.charAt(i);
      if (ch === '"' || ch === "'") {
        var end = out.indexOf(ch, i + 1);
        if (end < 0) end = out.length - 1;
        res += out.slice(i, end + 1);
        i = end + 1;
        continue;
      }
      if (ch === '%' && !isModulo(out, i)) {
        res += ' / 100';
        i++;
        continue;
      }
      res += ch;
      i++;
    }
    return res;
  }

  // --- что считать выражением ----------------------------------------------
  //
  // Блокнот, а не калькулятор: заголовки и пояснения между строками — обычное
  // дело, и краснеть на них нельзя. Строка идёт в math.js, только если похожа
  // на счёт; всё прочее молча остаётся без результата.

  function looksLikeExpression(line) {
    var text = line.trim();
    if (text === '') return false;
    if (text.charAt(0) === '#') return false;
    // Одинокое «_» — тоже выражение: это предыдущий результат.
    if (text === '_') return true;
    if (!/\d/.test(text) && !/[+\-*/^=]/.test(text)) return false;
    // «Итого:» и «Заметка — про счёт» выражениями не являются.
    if (/[:—]$/.test(text)) return false;
    return true;
  }

  // --- формат результата ---------------------------------------------------
  //
  // Точность — в значащих цифрах, как её понимает math.js. По умолчанию 12:
  // при 14 деление вроде 445/45*5 выдаёт хвост из одинаковых цифр во всю
  // строку, а при меньшем значении теряются копейки.

  var precision = 12;

  function setPrecision(digits) {
    var n = parseInt(digits, 10);
    if (n >= 1 && n <= 64)
      precision = n;
    return precision;
  }

  function format(value) {
    if (value === undefined || value === null) return '';
    if (typeof value === 'function') return '';
    if (typeof value === 'boolean') return value ? 'true' : 'false';
    if (typeof value === 'string') return value;
    try {
      return math.format(value, { precision: precision, lowerExp: -9, upperExp: 15 });
    } catch (e) {
      return String(value);
    }
  }

  // --- суммы по отступам ---------------------------------------------------
  //
  // Строка, оканчивающаяся двоеточием, складывает то, что под ней с большим
  // отступом. Считаются только прямые потомки — строки с наименьшим отступом
  // в блоке; всё, что глубже, уже вошло в их собственные суммы. Пустые строки
  // блок не заканчивают.

  function indentOf(line) {
    return /^[ \t]*/.exec(line)[0].replace(/\t/g, '    ').length;
  }

  function isLabel(line) {
    return /\S/.test(line) && /:\s*$/.test(line);
  }

  function applyBlockSums(lines, rows) {
    // С конца: вложенная метка стоит ниже внешней, значит к моменту, когда
    // дойдём до внешней, её собственная сумма уже посчитана.
    for (var i = lines.length - 1; i >= 0; i--) {
      if (!isLabel(lines[i])) continue;

      var own = indentOf(lines[i]);
      var end = i + 1;
      var inner = -1;
      while (end < lines.length) {
        if (lines[end].trim() === '') { end++; continue; }
        var depth = indentOf(lines[end]);
        if (depth <= own) break;
        if (inner < 0 || depth < inner) inner = depth;
        end++;
      }
      if (inner < 0) continue;

      var sum = null;
      for (var k = i + 1; k < end; k++) {
        if (lines[k].trim() === '' || indentOf(lines[k]) !== inner) continue;
        var value = rows[k].value;
        if (value === undefined || value === null) continue;
        try {
          sum = sum === null ? value : math.add(sum, value);
        } catch (e) {
          // Складывать несовместимое не будем — строка просто не участвует.
        }
      }
      if (sum === null) continue;

      rows[i].value = sum;
      rows[i].text = format(sum);
      rows[i].error = false;
    }
  }

  // --- построчный проход ---------------------------------------------------
  //
  // Область имён общая на весь текст: переменная, заданная выше, видна ниже.
  // Каждый успешный результат кладётся в last (и тем же значением в ans) —
  // так следующая строка может продолжить счёт, не повторяя предыдущую.

  function evaluate(text) {
    var parser = math.parser();
    var lines = String(text).split('\n');
    var rows = [];

    for (var i = 0; i < lines.length; i++) {
      var line = lines[i];
      if (line.trim() === '' || !looksLikeExpression(line)) {
        rows.push({ text: '', error: false, value: null });
        continue;
      }
      try {
        var value = parser.evaluate(expandPercents(line));
        var shown = format(value);
        if (shown !== '') {
          try {
            parser.set('last', value);
            parser.set('ans', value);
            // Короткое имя. math.js принимает «_» как обычное имя переменной,
            // поэтому ни подмены в тексте, ни оговорок здесь не нужно.
            parser.set('_', value);
          } catch (e) {
            // Имя могло быть занято пользователем — не повод ронять строку.
          }
        }
        rows.push({ text: shown, error: false, value: shown === '' ? null : value });
      } catch (err) {
        rows.push({ text: String(err.message || err), error: true, value: null });
      }
    }

    applyBlockSums(lines, rows);
    return rows;
  }

  return {
    evaluate: evaluate,
    setPrecision: setPrecision,
    expandPercents: expandPercents,
    looksLikeExpression: looksLikeExpression,
    format: format
  };
});
