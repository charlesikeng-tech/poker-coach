import { chunkFiles, selectImportableFiles } from './file-selection';

function file(name: string, size = 1): File {
  return new File([new Uint8Array(size)], name);
}

describe('selectImportableFiles', () => {
  it('keeps .txt and .zip files, whatever the case, and counts the others', () => {
    const selection = selectImportableFiles([
      file('hands.txt'),
      file('ARCHIVE.ZIP'),
      file('.DS_Store'),
      file('notes.pdf'),
    ]);

    expect(selection.accepted.map((f) => f.name)).toEqual(['hands.txt', 'ARCHIVE.ZIP']);
    expect(selection.ignored).toBe(2);
  });
});

describe('chunkFiles', () => {
  it('splits by file count', () => {
    const chunks = chunkFiles([file('1.txt'), file('2.txt'), file('3.txt')], 2, 1_000);

    expect(chunks.map((c) => c.length)).toEqual([2, 1]);
  });

  it('splits by size, and sends an oversized file alone', () => {
    const chunks = chunkFiles(
      [file('a.txt', 60), file('b.txt', 60), file('big.txt', 500)],
      10,
      100,
    );

    expect(chunks.map((c) => c.map((f) => f.name))).toEqual([['a.txt'], ['b.txt'], ['big.txt']]);
  });

  it('returns no chunk for no file', () => {
    expect(chunkFiles([])).toEqual([]);
  });
});
