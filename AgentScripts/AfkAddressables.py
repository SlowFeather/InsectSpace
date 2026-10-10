import argparse
import json
import struct
from pathlib import Path


class Catalog:
    def __init__(self, path):
        self.data = path.read_bytes()
        self.root = self.u32(0)
        self.vtable = self.root - self.i32(self.root)
        self.ids = self.strings(5)
        self.keys_data = self.bytes(6)
        self.entries_data = self.bytes(8)
        buckets = self.bytes(7)
        count = struct.unpack_from("<I", buckets)[0]
        self.keys, self.buckets = [], []
        pos = 4
        for _ in range(count):
            key_offset, size = struct.unpack_from("<II", buckets, pos)
            pos += 8
            self.buckets.append(list(struct.unpack_from("<" + "i" * size, buckets, pos)))
            pos += 4 * size
            self.keys.append(self.key(key_offset))
        assert pos == len(buckets)
        count = struct.unpack_from("<I", self.entries_data)[0]
        assert len(self.entries_data) == 4 + count * 24
        self.entries = [struct.unpack_from("<6i", self.entries_data, 4 + i * 24) for i in range(count)]

    def u32(self, p):
        return struct.unpack_from("<I", self.data, p)[0]

    def i32(self, p):
        return struct.unpack_from("<i", self.data, p)[0]

    def target(self, index):
        off = struct.unpack_from("<H", self.data, self.vtable + 4 + index * 2)[0]
        p = self.root + off
        return p + self.u32(p)

    def bytes(self, index):
        p = self.target(index)
        return self.data[p+4:p+4+self.u32(p)]

    def strings(self, index):
        p = self.target(index)
        values = []
        for i in range(self.u32(p)):
            q = p + 4 + i * 4
            s = q + self.u32(q)
            values.append(self.data[s+4:s+4+self.u32(s)].decode("utf-8"))
        return values

    def key(self, p):
        kind = self.keys_data[p]
        if kind in (0, 1):
            length = struct.unpack_from("<I", self.keys_data, p+1)[0]
            return self.keys_data[p+5:p+5+length].decode("ascii" if kind == 0 else "utf-16-le")
        if kind in (2, 3, 4):
            return struct.unpack_from({2: "<H", 3: "<I", 4: "<i"}[kind], self.keys_data, p+1)[0]
        return {"kind": kind, "offset": p}

    def location(self, index):
        entry = self.entries[index]
        return {"index": index, "id": self.ids[entry[0]], "entry": entry,
                "key": self.keys[entry[4]]}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("path", type=Path)
    parser.add_argument("query")
    parser.add_argument("--limit", type=int, default=40)
    args = parser.parse_args()
    catalog = Catalog(args.path)
    found = [(i, key) for i, key in enumerate(catalog.keys) if isinstance(key, str) and args.query.lower() in key.lower()]
    print(json.dumps({"ids": len(catalog.ids), "keys": len(catalog.keys), "entries": len(catalog.entries),
                      "matches": len(found), "locations": [{"key": key, "key_index": i,
                        "locations": [catalog.location(j) for j in catalog.buckets[i]]} for i,key in found[:args.limit]]}, indent=2))
