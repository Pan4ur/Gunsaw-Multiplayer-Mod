import base64
import hashlib
import json
import pathlib
import re
import zlib

ROM_UID_PREFIX = "ROM/"
CUSTOM_PROP_PATH = "MP/CustomProp"
LOGIC_TYPE_PREFIX = "MP/Logic/"
INSERT_INDEX = 756

def decode_level(path: pathlib.Path) -> dict:
    text = path.read_text().strip()
    if text.startswith("{"):
        return json.loads(text)
    return json.loads(zlib.decompress(base64.b64decode(text), -15).decode())

def encode_level(level: dict) -> str:
    raw = json.dumps(level, ensure_ascii=False, separators=(",", ":")).encode()
    compressor = zlib.compressobj(level=1, wbits=-15)
    compressed = compressor.compress(raw) + compressor.flush()
    return base64.b64encode(compressed).decode()

def get_custom_prop_metadata(part: dict):
    if part.get("path") != CUSTOM_PROP_PATH:
        return None, None
    try:
        metadata = json.loads(part["team"])
        return metadata, json.loads(metadata["data"])
    except (KeyError, TypeError, json.JSONDecodeError):
        return None, None

def is_rom_part(part: dict) -> bool:
    metadata, _ = get_custom_prop_metadata(part)
    return bool(metadata and metadata.get("uid", "").startswith(ROM_UID_PREFIX))

def create_gate(x, y, uid: str, gate_type: str, data: dict) -> dict:
    metadata = {
        "version": 1,
        "uid": uid,
        "type": LOGIC_TYPE_PREFIX + gate_type,
        "data": json.dumps(data, separators=(",", ":")),
    }
    return {
        "pos": {"x": round(float(x) * 4) / 4, "y": round(float(y) * 4) / 4},
        "rot": 0.0,
        "path": CUSTOM_PROP_PATH,
        "id": 0,
        "activId": 0,
        "team": json.dumps(metadata, separators=(",", ":")),
        "size": {"x": 0.0, "y": 0.0},
        "force": {"x": 0.0, "y": 0.0},
    }

def calculate_hardware_hash(level: dict) -> str:
    hardware_parts = [part for part in level["parts"] if not is_rom_part(part)]
    raw = json.dumps(hardware_parts, ensure_ascii=False, separators=(",", ":")).encode()
    return hashlib.sha256(raw).hexdigest()

def parse_asm(path: pathlib.Path):
    rom_parts = []
    rom_name = path.stem
    has_header = False

    for line_number, raw_line in enumerate(path.read_text().splitlines(), start=1):
        line = raw_line.strip()

        if not line or line.startswith((";", "#")):
            continue
        if line == "U8ASM 1":
            has_header = True
            continue
        if line.startswith("NAME "):
            rom_name = json.loads(line[5:].strip())
            continue
        if not line.startswith("GATE "):
            raise ValueError(f"{path}:{line_number}: unknown directive")

        match = re.fullmatch(
            r'GATE\s+(\S+)\s+(\S+)\s+("(?:[^"\\]|\\.)*")\s+(\S+)\s+(.+)',
            line,
        )

        if not match:
            raise ValueError(f"{path}:{line_number}: malformed GATE")

        x, y, uid_json, gate_type, data_json = match.groups()
        uid = json.loads(uid_json)
        data = json.loads(data_json)

        if not uid.startswith(ROM_UID_PREFIX):
            raise ValueError(f"{path}:{line_number}: UID must start with {ROM_UID_PREFIX}")

        rom_parts.append(create_gate(x, y, uid, gate_type, data))

    if not has_header:
        raise ValueError("missing U8ASM 1 header")

    uids = [get_custom_prop_metadata(part)[0]["uid"] for part in rom_parts]
    if len(uids) != len(set(uids)):
        raise ValueError("duplicate ROM uid")

    return rom_name, rom_parts

def build(asm_path: pathlib.Path, base_path: pathlib.Path, output_path: pathlib.Path):
    rom_name, rom_parts = parse_asm(asm_path)
    level = decode_level(base_path)
    hardware_hash_before = calculate_hardware_hash(level)

    hardware_parts = [part for part in level["parts"] if not is_rom_part(part)]
    insert_index = len(hardware_parts)

    insert_index = max(0, min(INSERT_INDEX, len(hardware_parts)))

    level["parts"] = (
        hardware_parts[:insert_index]
        + rom_parts
        + hardware_parts[insert_index:]
    )

    hardware_hash_after = calculate_hardware_hash(level)

    if hardware_hash_before != hardware_hash_after:
        raise RuntimeError("non-ROM hardware hash changed")

    encoded = encode_level(level)
    decoded = json.loads(zlib.decompress(base64.b64decode(encoded), -15).decode())

    if decoded != level:
        raise RuntimeError("round-trip failed")

    output_path.write_text(encoded)
    print(f"Built {asm_path.name} -> {output_path.name} ({len(rom_parts)} gates)")

def main():
    root = pathlib.Path(__file__).resolve().parent
    roms = root / "roms"
    builds = root / "builds"
    base = root / "hardware" / "base.txt"

    builds.mkdir(exist_ok=True)

    asm_files = sorted(roms.glob("*.asm"))

    for asm_path in asm_files:
        output_path = builds / f"{asm_path.stem}.txt"
        build(asm_path, base, output_path)

    print("Done!")

if __name__ == "__main__":
    main()