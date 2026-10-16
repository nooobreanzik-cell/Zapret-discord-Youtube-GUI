"""Repack the supplied NSIS solid-LZMA installer with edited C# sources.

The installer compiles these sources with Windows Framework csc.exe.
Keep the PE stub and uninstaller bytes unchanged; remap data block references.
"""
from pathlib import Path
import struct
import lzma
import zlib
import hashlib
import json

original = Path('ZapretGui-0.9.11/ZapretGui-Setup-0.9.11.exe').read_bytes()
signature = b'\xef\xbe\xad\xdeNullsoftInst'
start = original.index(signature) - 4
first = list(struct.unpack_from('<7I', original, start))
assert start + first[6] == len(original)
assert zlib.crc32(original[512:-4]) == struct.unpack('<I', original[-4:])[0]
filters = [{'id': lzma.FILTER_LZMA1, 'dict_size': 8388608, 'lc': 3, 'lp': 0, 'pb': 2}]
assert original[start+28:start+33] == bytes.fromhex('5d00008000')
raw = lzma.decompress(original[start+33:-4], format=lzma.FORMAT_RAW, filters=filters)
header_size = struct.unpack_from('<I', raw)[0]
assert header_size == first[5]
header = bytearray(raw[4:4+header_size])
entries, count = struct.unpack_from('<II', header, 20)
strings = struct.unpack_from('<I', header, 28)[0]
blocks = {}
pos = header_size + 4
while pos < len(raw):
    size = struct.unpack_from('<I', raw, pos)[0]
    blocks[pos-header_size-4] = raw[pos+4:pos+4+size]
    assert len(blocks[pos-header_size-4]) == size
    pos += size + 4
assert pos == len(raw)
original_blocks = dict(blocks)

def string(index):
    begin = strings + index * 2
    end = begin
    while header[end:end+2] != b'\0\0':
        end += 2
    return header[begin:end].decode('utf-16le')

package = Path('ZapretGui-0.9.15')
changed = []
for index in range(427, 443):
    entry = struct.unpack_from('<7i', header, entries + index*28)
    assert entry[0] == 20
    name = string(entry[2])
    revised = (package/'source'/name).read_bytes()
    if revised != blocks[entry[3]]:
        changed.append(name)
    blocks[entry[3]] = revised

# Include the four domain suffixes in the bundled default general list.
# Existing user lists are managed separately by the GUI.
for index in range(count):
    entry = struct.unpack_from('<7i', header, entries + index*28)
    if entry[0] == 20 and string(entry[2]) == 'list-general.txt':
        text = blocks[entry[3]].decode('utf-8-sig')
        names = {line.split('#')[0].strip().lower() for line in text.splitlines()}
        missing = [d for d in ('chatgpt.com', 'openai.com', 'oaistatic.com', 'oaiusercontent.com') if d not in names]
        if missing:
            text = text.rstrip() + '\r\n# ChatGPT domains (Zapret GUI)\r\n' + '\r\n'.join(missing) + '\r\n'
            blocks[entry[3]] = text.encode('utf-8')
        (package/'defaults').mkdir(exist_ok=True)
        (package/'defaults'/'list-general.txt').write_bytes(blocks[entry[3]])

for index in range(528, 533):
    entry = struct.unpack_from('<7i', header, entries + index*28)
    (package/'licenses'/string(entry[2])).write_bytes(blocks[entry[3]])
entry = struct.unpack_from('<7i', header, entries + 535*28)
(package/'NOTICE-original.txt').write_bytes(blocks[entry[3]])

# Separate artwork for the welcome and finish page extraction instructions.
art_by_entry = {2: 0, 154: -1}
blocks[0] = (package/'installer-art'/'welcome.bmp').read_bytes()
items = list(blocks.items())
blocks = dict(items[:-2] + [(-1, (package/'installer-art'/'finish.bmp').read_bytes())] + items[-2:])
for index, block in art_by_entry.items():
    loc = entries + index*28
    entry = list(struct.unpack_from('<7i', header, loc))
    assert entry[0] == 20 and entry[3] == 0
    entry[3] = block
    struct.pack_into('<7i', header, loc, *entry)

remap = {}
data = bytearray()
for old_offset, content in blocks.items():
    remap[old_offset] = len(data)
    data += struct.pack('<I', len(content)) + content

references = 0
for index in range(count):
    loc = entries + index*28
    entry = list(struct.unpack_from('<7i', header, loc))
    if entry[0] == 20:  # EW_EXTRACTFILE: offsets[2] is data offset.
        entry[3] = remap[entry[3]]
        references += 1
    elif entry[0] == 62:  # EW_WRITEUNINSTALLER: offsets[1] is data offset.
        entry[2] = remap[entry[2]]
        references += 1
    struct.pack_into('<7i', header, loc, *entry)

# Same-length replacements preserve all UTF-16 string references.
header = header.replace('0.6'.encode('utf-16le'), '0.9'.encode('utf-16le'))
header = header.replace('0.4'.encode('utf-16le'), '0.9'.encode('utf-16le'))
assert len(header) == header_size
# Store a distinct finish filename and update both extraction and image loading.
# Appending preserves every existing header block and string offset.
finish_name = string(163).replace('modern-wizard.bmp', 'modern-finish.bmp')
assert finish_name != string(163) and (len(header) - strings) % 2 == 0
finish_index = (len(header) - strings) // 2
header += (finish_name + '\0').encode('utf-16le')
for index, opcode in ((154, 20), (184, 37)):
    loc = entries + index*28
    entry = list(struct.unpack_from('<7i', header, loc))
    assert entry[0] == opcode and entry[2] == 163
    entry[2] = finish_index
    struct.pack_into('<7i', header, loc, *entry)
header_size = len(header)
first[5] = header_size
assert string(finish_index) == finish_name
new_raw = struct.pack('<I', header_size) + header + data
compressed = bytes.fromhex('5d00008000') + lzma.compress(new_raw, format=lzma.FORMAT_RAW, filters=filters)
first[6] = 28 + len(compressed) + 4
result = original[:start] + struct.pack('<7I', *first) + compressed
result += struct.pack('<I', zlib.crc32(result[512:]))
out = package/'ZapretGui-Setup-0.9.15.exe'
out.write_bytes(result)

# Round-trip the delivered installer, then resolve every extraction reference.
check = out.read_bytes()
assert check[:start] == original[:start]
assert zlib.crc32(check[512:-4]) == struct.unpack('<I', check[-4:])[0]
assert len(check) == start + struct.unpack_from('<I', check, start+24)[0]
decoded = lzma.decompress(check[start+33:-4], format=lzma.FORMAT_RAW, filters=filters)
assert decoded == new_raw
for index in range(count):
    old_entry = struct.unpack_from('<7i', raw, 4+entries+index*28)
    new_entry = struct.unpack_from('<7i', decoded, 4+entries+index*28)
    operand = 3 if old_entry[0] == 20 else 2 if old_entry[0] == 62 else None
    if operand is not None:
        loc = 4 + header_size + new_entry[operand]
        size = struct.unpack_from('<I', decoded, loc)[0]
        assert decoded[loc+4:loc+4+size] == blocks[art_by_entry.get(index, old_entry[operand])]
assert list(blocks.values())[-2:] == list(original_blocks.values())[-2:]
summary = {'changed_sources': changed, 'data_blocks': len(blocks),
           'validated_references': references, 'installer_bytes': len(result),
           'sha256': hashlib.sha256(result).hexdigest(),
           'windows_runtime_tested': False}
(package/'validation.json').write_text(json.dumps(summary, indent=2)+'\n')
print(json.dumps(summary, indent=2))
