#!/usr/bin/env python3
"""Convierte un Markdown (subconjunto usado en los documentos de MiSaldo) a HTML
imprimible con estilos. No requiere dependencias externas.

Soporta: encabezados (#..######), tablas GitHub, listas (-, 1.), citas (>),
reglas horizontales (---), negrita (**...**), código en línea (`...`),
enlaces [texto](url) y párrafos.

Uso: python3 md_to_html.py entrada.md salida.html "Título del documento"
"""
import html
import re
import sys


def inline(text: str) -> str:
    text = html.escape(text)
    # negrita
    text = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", text)
    # cursiva (evita colisionar con negrita ya procesada)
    text = re.sub(r"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", r"<em>\1</em>", text)
    # código en línea
    text = re.sub(r"`(.+?)`", r"<code>\1</code>", text)
    # enlaces
    text = re.sub(r"\[(.+?)\]\((.+?)\)", r'<a href="\2">\1</a>', text)
    return text


def split_row(row: str):
    row = row.strip()
    if row.startswith("|"):
        row = row[1:]
    if row.endswith("|"):
        row = row[:-1]
    return [c.strip() for c in row.split("|")]


def convert(md: str) -> str:
    lines = md.split("\n")
    out = []
    i = 0
    n = len(lines)
    in_list = False

    def close_list():
        nonlocal in_list
        if in_list:
            out.append("</ul>")
            in_list = False

    while i < n:
        line = lines[i]
        stripped = line.strip()

        # línea en blanco
        if not stripped:
            close_list()
            i += 1
            continue

        # regla horizontal
        if re.fullmatch(r"-{3,}", stripped):
            close_list()
            out.append("<hr/>")
            i += 1
            continue

        # encabezados
        m = re.match(r"^(#{1,6})\s+(.*)$", stripped)
        if m:
            close_list()
            level = len(m.group(1))
            out.append(f"<h{level}>{inline(m.group(2))}</h{level}>")
            i += 1
            continue

        # cita
        if stripped.startswith(">"):
            close_list()
            quote_lines = []
            while i < n and lines[i].strip().startswith(">"):
                quote_lines.append(lines[i].strip()[1:].strip())
                i += 1
            out.append(f"<blockquote>{inline(' '.join(quote_lines))}</blockquote>")
            continue

        # tabla
        if stripped.startswith("|") and i + 1 < n and re.match(r"^\s*\|?[\s:\-|]+\|?\s*$", lines[i + 1]):
            close_list()
            header = split_row(lines[i])
            i += 2  # saltar encabezado y separador
            rows = []
            while i < n and lines[i].strip().startswith("|"):
                rows.append(split_row(lines[i]))
                i += 1
            out.append("<table>")
            out.append("<thead><tr>" + "".join(f"<th>{inline(c)}</th>" for c in header) + "</tr></thead>")
            out.append("<tbody>")
            for r in rows:
                out.append("<tr>" + "".join(f"<td>{inline(c)}</td>" for c in r) + "</tr>")
            out.append("</tbody></table>")
            continue

        # lista
        if re.match(r"^[-*]\s+", stripped) or re.match(r"^\d+\.\s+", stripped):
            if not in_list:
                out.append("<ul>")
                in_list = True
            item = re.sub(r"^([-*]|\d+\.)\s+", "", stripped)
            out.append(f"<li>{inline(item)}</li>")
            i += 1
            continue

        # párrafo
        close_list()
        out.append(f"<p>{inline(stripped)}</p>")
        i += 1

    close_list()
    return "\n".join(out)


CSS = """
@page { size: A4; margin: 2.2cm 2cm; }
body { font-family: 'Liberation Sans', Arial, sans-serif; font-size: 11pt; color: #1a1a1a; line-height: 1.5; }
h1 { font-size: 20pt; color: #0b5d4a; border-bottom: 2px solid #0b5d4a; padding-bottom: 6px; }
h2 { font-size: 14pt; color: #0b5d4a; margin-top: 22px; }
h3 { font-size: 12pt; color: #333; margin-top: 16px; }
p { margin: 8px 0; text-align: justify; }
a { color: #0b5d4a; }
code { background: #f0f0f0; padding: 1px 4px; border-radius: 3px; font-family: 'Liberation Mono', monospace; font-size: 10pt; }
blockquote { border-left: 4px solid #0b5d4a; background: #f3f8f6; margin: 12px 0; padding: 8px 14px; color: #333; }
table { border-collapse: collapse; width: 100%; margin: 12px 0; font-size: 10pt; }
th, td { border: 1px solid #c5c5c5; padding: 6px 8px; text-align: left; vertical-align: top; }
th { background: #0b5d4a; color: #fff; }
tr:nth-child(even) td { background: #f6f6f6; }
hr { border: none; border-top: 1px solid #ddd; margin: 18px 0; }
ul { margin: 8px 0; padding-left: 22px; }
li { margin: 4px 0; }
"""


def main():
    src, dst, title = sys.argv[1], sys.argv[2], sys.argv[3]
    with open(src, encoding="utf-8") as f:
        md = f.read()
    body = convert(md)
    doc = f"""<!DOCTYPE html>
<html lang="es">
<head>
<meta charset="utf-8"/>
<title>{html.escape(title)}</title>
<style>{CSS}</style>
</head>
<body>
{body}
</body>
</html>
"""
    with open(dst, "w", encoding="utf-8") as f:
        f.write(doc)
    print(f"HTML generado: {dst}")


if __name__ == "__main__":
    main()
