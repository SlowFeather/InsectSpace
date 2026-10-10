"""Read-only inspection of the local reference map's FlatBuffers."""
import argparse
import array
import collections
import json
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[1] / ".artifacts/reference/afk-journey"


class Buffer:
    def __init__(self, path):
        self.data = Path(path).read_bytes()

    def number(self, offset, fmt="I"):
        return struct.unpack_from("<" + fmt, self.data, offset)[0]

    def fields(self, table):
        vtable = table - self.number(table, "i")
        if not 0 <= vtable < len(self.data) - 4:
            raise ValueError(f"Invalid vtable at {table}")
        length = self.number(vtable, "H")
        assert 4 <= length <= 256 and length % 2 == 0
        return [table + self.number(vtable + 4 + i * 2, "H")
                if self.number(vtable + 4 + i * 2, "H") else None
                for i in range((length - 4) // 2)]

    def target(self, offset):
        return offset + self.number(offset)

    def describe(self, table):
        result = dict(table=table, fields=[])
        for i, p in enumerate(self.fields(table)):
            if p is None:
                continue
            record = dict(index=i, offset=p-table, bytes=self.data[p:p+24].hex())
            if p+4 <= len(self.data):
                record.update(u32=self.number(p), f32=self.number(p, "f"))
                q = self.target(p)
                if p < q < len(self.data)-4:
                    record.update(target=q, target_head=self.data[q:q+32].hex(), count=self.number(q))
            result["fields"].append(record)
        return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("name")
    parser.add_argument("--table", type=int)
    parser.add_argument("--vector", type=int)
    parser.add_argument("--limit", type=int, default=3)
    args = parser.parse_args()
    b = Buffer(ROOT / "map-source" / args.name)
    table = args.table if args.table is not None else b.number(0)
    print(json.dumps(b.describe(table), indent=2))
    if args.vector is not None:
        p = b.target(b.fields(table)[args.vector])
        print("vector", p, "length", b.number(p))
        for i in range(min(b.number(p), args.limit)):
            q = p+4+i*4
            print(json.dumps(b.describe(b.target(q)), indent=2))


if __name__ == "__main__":
    main()
