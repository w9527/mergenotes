import base64
import hashlib
import json
import os
import shutil
import sqlite3
import tempfile
import uuid
import zipfile
from datetime import datetime, timezone


SOURCE_ZIP = r"D:\Downloads\UserdataBackup_2026-07-05_DESKTOP-84M5US2.jwlibrary"
OUT_FILE = os.path.join(r"D:\Documents\mergenotes", "_blank.jwlibrary")


def utc_now_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def local_now_iso() -> str:
    return datetime.now().astimezone().isoformat(timespec="seconds")


def main() -> None:
    workdir = os.path.join(tempfile.gettempdir(), "blank_jwlibrary_build")
    shutil.rmtree(workdir, ignore_errors=True)
    os.makedirs(workdir, exist_ok=True)

    with zipfile.ZipFile(SOURCE_ZIP) as zf:
        zf.extractall(workdir)

    db_path = os.path.join(workdir, "userData.db")
    con = sqlite3.connect(db_path)
    con.execute("PRAGMA foreign_keys=OFF")
    cur = con.cursor()

    delete_order = [
        "TagMap",
        "BlockRange",
        "Bookmark",
        "InputField",
        "Note",
        "UserMark",
        "IndependentMedia",
        "PlaylistItemIndependentMediaMap",
        "PlaylistItemLocationMap",
        "PlaylistItemMarkerBibleVerseMap",
        "PlaylistItemMarkerParagraphMap",
        "PlaylistItemMarker",
        "PlaylistItem",
        "Tag",
        "Location",
    ]
    for table in delete_order:
        cur.execute(f"DELETE FROM [{table}]")

    location_id = 1
    user_mark_id = 1
    note_id = 1
    tag_id = 1
    bookmark_id = 1
    block_range_id = 1
    independent_media_id = 1

    cur.execute(
        "INSERT INTO Location (LocationId, BookNumber, ChapterNumber, DocumentId, Track, IssueTagNumber, KeySymbol, MepsLanguage, Type, Title, Specialty, Edition) "
        "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
        (
            location_id,
            None,
            None,
            123456789,
            None,
            0,
            "demo",
            279,
            0,
            "示例章节",
            None,
            None,
        ),
    )

    cur.execute(
        "INSERT INTO UserMark (UserMarkId, ColorIndex, LocationId, StyleIndex, UserMarkGuid, Version) VALUES (?, ?, ?, ?, ?, ?)",
        (user_mark_id, 1, location_id, 0, str(uuid.uuid4()), 1),
    )

    now_utc = utc_now_iso()
    cur.execute(
        "INSERT INTO Note (NoteId, Guid, UserMarkId, LocationId, Title, Content, LastModified, Created, BlockType, BlockIdentifier) "
        "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
        (
            note_id,
            str(uuid.uuid4()),
            user_mark_id,
            location_id,
            "随机测试笔记",
            "这是一条用于导入验证的示例笔记，包含标题、标签、书签和附件元数据。",
            now_utc,
            now_utc,
            1,
            1,
        ),
    )

    cur.execute(
        "INSERT INTO Tag (TagId, Type, Name) VALUES (?, ?, ?)",
        (tag_id, 0, "DemoTag"),
    )

    cur.execute(
        "INSERT INTO TagMap (TagMapId, PlaylistItemId, LocationId, NoteId, TagId, Position) VALUES (?, ?, ?, ?, ?, ?)",
        (1, None, None, note_id, tag_id, 0),
    )

    cur.execute(
        "INSERT INTO Bookmark (BookmarkId, LocationId, PublicationLocationId, Slot, Title, Snippet, BlockType, BlockIdentifier) "
        "VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
        (
            bookmark_id,
            location_id,
            location_id,
            0,
            "示例书签",
            "用于验证导入流程的书签。",
            1,
            1,
        ),
    )

    cur.execute(
        "INSERT INTO BlockRange (BlockRangeId, BlockType, Identifier, StartToken, EndToken, UserMarkId) VALUES (?, ?, ?, ?, ?, ?)",
        (block_range_id, 1, 1, 0, 24, user_mark_id),
    )

    png_name = "2e36d4f7-4a5d-4d7f-8b89-bf8f0d4c1d23"
    png_bytes = base64.b64decode(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO7+X9kAAAAASUVORK5CYII="
    )
    with open(os.path.join(workdir, png_name), "wb") as f:
        f.write(png_bytes)
    cur.execute(
        "INSERT INTO IndependentMedia (IndependentMediaId, OriginalFilename, FilePath, MimeType, Hash) VALUES (?, ?, ?, ?, ?)",
        (
            independent_media_id,
            "blank_attachment.png",
            png_name,
            "image/png",
            hashlib.sha256(png_bytes).hexdigest(),
        ),
    )

    cur.execute(
        "INSERT INTO InputField (LocationId, TextTag, Value) VALUES (?, ?, ?)",
        (location_id, "ttdemo", "1"),
    )

    cur.execute("UPDATE LastModified SET LastModified = ?", (utc_now_iso(),))

    con.commit()
    con.close()

    thumbnail = base64.b64decode(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO7+X9kAAAAASUVORK5CYII="
    )
    with open(os.path.join(workdir, "default_thumbnail.png"), "wb") as f:
        f.write(thumbnail)

    with open(db_path, "rb") as f:
        db_hash = hashlib.sha256(f.read()).hexdigest()

    manifest = {
        "name": "_blank.jwlibrary",
        "creationDate": local_now_iso(),
        "version": 1,
        "type": 0,
        "userDataBackup": {
            "lastModifiedDate": utc_now_iso(),
            "deviceName": "CODex-Demo",
            "databaseName": "userData.db",
            "hash": db_hash,
            "schemaVersion": 16,
        },
    }
    with open(os.path.join(workdir, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, separators=(",", ":"))

    with zipfile.ZipFile(OUT_FILE, "w", compression=zipfile.ZIP_DEFLATED) as zf:
        for filename in ["manifest.json", "userData.db", "default_thumbnail.png", png_name]:
            zf.write(os.path.join(workdir, filename), arcname=filename)

    print(OUT_FILE)


if __name__ == "__main__":
    main()
