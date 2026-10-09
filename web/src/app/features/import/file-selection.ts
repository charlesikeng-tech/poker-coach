/** Extensions the API accepts. Anything else in a dropped folder (.DS_Store, logs…) is left out. */
const ACCEPTED_EXTENSIONS = ['.txt', '.zip'];

/**
 * Upload chunks stay well under the API limits (500 files, 100 MB per request), so that dropping a
 * whole history folder works without the user knowing about limits.
 */
export const MAX_FILES_PER_CHUNK = 200;
export const MAX_BYTES_PER_CHUNK = 40 * 1024 * 1024;

export interface FileSelection {
  readonly accepted: readonly File[];
  readonly ignored: number;
}

export function selectImportableFiles(files: readonly File[]): FileSelection {
  const accepted = files.filter((file) =>
    ACCEPTED_EXTENSIONS.some((extension) => file.name.toLowerCase().endsWith(extension)),
  );
  return { accepted, ignored: files.length - accepted.length };
}

/** Groups files into upload requests; a file larger than a chunk travels alone (the API judges it). */
export function chunkFiles(
  files: readonly File[],
  maxFiles = MAX_FILES_PER_CHUNK,
  maxBytes = MAX_BYTES_PER_CHUNK,
): File[][] {
  const chunks: File[][] = [];
  let current: File[] = [];
  let currentBytes = 0;
  for (const file of files) {
    if (current.length > 0 && (current.length >= maxFiles || currentBytes + file.size > maxBytes)) {
      chunks.push(current);
      current = [];
      currentBytes = 0;
    }
    current.push(file);
    currentBytes += file.size;
  }
  if (current.length > 0) {
    chunks.push(current);
  }
  return chunks;
}

/**
 * Files of a drop, folders included (walked recursively). Entries must be read synchronously, inside
 * the drop event: the DataTransfer is emptied once the handler returns.
 */
export function filesFromDrop(dataTransfer: DataTransfer): Promise<File[]> {
  const entries = Array.from(dataTransfer.items)
    .filter((item) => item.kind === 'file')
    .map((item) => item.webkitGetAsEntry())
    .filter((entry): entry is FileSystemEntry => entry !== null);

  if (entries.length === 0) {
    return Promise.resolve(Array.from(dataTransfer.files));
  }
  return collectFiles(entries);
}

async function collectFiles(entries: readonly FileSystemEntry[]): Promise<File[]> {
  const files: File[] = [];
  for (const entry of entries) {
    if (isFileEntry(entry)) {
      files.push(await new Promise<File>((resolve, reject) => entry.file(resolve, reject)));
    } else if (isDirectoryEntry(entry)) {
      files.push(...(await collectFiles(await readAllEntries(entry))));
    }
  }
  return files;
}

/** readEntries returns at most ~100 entries per call: read until it returns none. */
async function readAllEntries(directory: FileSystemDirectoryEntry): Promise<FileSystemEntry[]> {
  const reader = directory.createReader();
  const all: FileSystemEntry[] = [];
  for (;;) {
    const batch = await new Promise<FileSystemEntry[]>((resolve, reject) =>
      reader.readEntries(resolve, reject),
    );
    if (batch.length === 0) {
      return all;
    }
    all.push(...batch);
  }
}

function isFileEntry(entry: FileSystemEntry): entry is FileSystemFileEntry {
  return entry.isFile;
}

function isDirectoryEntry(entry: FileSystemEntry): entry is FileSystemDirectoryEntry {
  return entry.isDirectory;
}
