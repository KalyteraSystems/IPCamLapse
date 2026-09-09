const fs = require('node:fs');
const vm = require('node:vm');

function functionSource(page, name) {
  const start = page.indexOf(`function ${name}(`);
  if (start < 0) throw new Error(`Could not find ${name}`);
  const openingBrace = page.indexOf('{', start);
  let depth = 0;
  for (let index = openingBrace; index < page.length; index += 1) {
    if (page[index] === '{') depth += 1;
    if (page[index] === '}' && --depth === 0) return page.slice(start, index + 1);
  }
  throw new Error(`Could not parse ${name}`);
}

class Element {
  constructor(tagName) {
    this.tagName = tagName;
    this.children = [];
  }

  appendChild(child) {
    this.children.push(child);
    return child;
  }

  append(...children) {
    children.forEach(child => this.appendChild(child));
  }
}

const gallery = new Element('div');
global.document = {
  createElement: tagName => new Element(tagName),
  getElementById: id => id === 'frame-gallery' ? gallery : null
};

const page = fs.readFileSync(process.argv[2], 'utf8');
vm.runInThisContext(functionSource(page, 'formatFrameSize'));
vm.runInThisContext(functionSource(page, 'addFrame'));

function dynamicLabelFor(sizeBytes) {
  gallery.children = [];
  addFrame({
    number: 25,
    capturedAt: '2026-09-04T12:00:00Z',
    sizeBytes,
    previewUrl: '/preview.jpg',
    downloadUrl: '/download.jpg'
  });
  return gallery.children[0].children[1].children[0].textContent;
}

if (!dynamicLabelFor(2_560).endsWith('3 KB')) throw new Error('2.5 KiB dynamic card must render as 3 KB');
if (!dynamicLabelFor(1_572_864).endsWith('1.5 MB')) throw new Error('1.5 MiB dynamic card must render as 1.5 MB');
