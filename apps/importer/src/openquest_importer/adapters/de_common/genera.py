"""German common tree names → Latin genus.

German open data often names trees by their common name ("Linde") instead of
the Latin genus. The table covers the names used by Straßen.NRW and the
Münster natural monument register. Names that stand for more than one genus
map to ``None`` and are reported as ambiguous.
"""

from __future__ import annotations

import re

#: Common name (lower case) → Latin genus; ``None`` means ambiguous.
COMMON_NAMES: dict[str, str | None] = {
    "ahorn": "Acer", "bergahorn": "Acer", "feldahorn": "Acer", "spitzahorn": "Acer", "silberahorn": "Acer",
    "amberbaum": "Liquidambar",
    "baumhasel": "Corylus", "hasel": "Corylus",
    "birke": "Betula", "sandbirke": "Betula",
    "birne": "Pyrus", "stadtbirne": "Pyrus",
    "buche": "Fagus", "rotbuche": "Fagus", "blutbuche": "Fagus", "hängebuche": "Fagus",
    "catalpa": "Catalpa", "trompetenbaum": "Catalpa",
    "crataegus": "Crataegus", "weißdorn": "Crataegus", "rotdorn": "Crataegus",
    "douglasie": "Pseudotsuga",
    "eberesche": "Sorbus", "mehlbeere": "Sorbus", "elsbeere": "Sorbus",
    "eibe": "Taxus",
    "eiche": "Quercus", "stieleiche": "Quercus", "stiel-eiche": "Quercus", "traubeneiche": "Quercus",
    "roteiche": "Quercus", "sumpfeiche": "Quercus",
    "erle": "Alnus", "schwarzerle": "Alnus",
    "esche": "Fraxinus",
    "fichte": "Picea",
    "ginko": "Ginkgo", "ginkgo": "Ginkgo",
    "gleditschie": "Gleditsia", "lederhülsenbaum": "Gleditsia",
    "götterbaum": "Ailanthus",
    "hainbuche": "Carpinus",
    "kiefer": "Pinus",
    "kirsche": "Prunus", "vogelkirsche": "Prunus", "zierkirsche": "Prunus",
    "lärche": "Larix",
    "linde": "Tilia", "sommerlinde": "Tilia", "winterlinde": "Tilia", "silberlinde": "Tilia", "kaiserlinde": "Tilia",
    "mispel": "Mespilus",
    "pappel": "Populus",
    "platane": "Platanus",
    "robinie": "Robinia",
    "rosskastanie": "Aesculus", "roßkastanie": "Aesculus",
    "scheinzypresse": "Chamaecyparis",
    "schnurbaum": "Styphnolobium",
    "schwarznuss": "Juglans", "walnuss": "Juglans",
    "sumpfzypresse": "Taxodium", "sumpfzypress": "Taxodium",
    "tanne": "Abies",
    "thuja": "Thuja", "lebensbaum": "Thuja",
    "tulpenbaum": "Liriodendron",
    "ulme": "Ulmus",
    "wacholder": "Juniperus",
    "weide": "Salix",
    "zeder": "Cedrus",
    "zierapfel": "Malus", "apfel": "Malus",
    # Less common names from the Münster natural monument register
    "esskastanie": "Castanea", "edelkastanie": "Castanea",
    "fächerblattbaum": "Ginkgo",
    "flügelnuss": "Pterocarya",
    "judasbaum": "Cercis",
    "magnolie": "Magnolia", "tulpenmagnolie": "Magnolia",
    "maulbeerbaum": "Morus",
    "sicheltanne": "Cryptomeria",
    "stechpalme": "Ilex",
    "urweltmammutbaum": "Metasequoia",
    # Ambiguous: several genera share the name
    "kastanie": None,  # Aesculus (horse chestnut) or Castanea (sweet chestnut)
    "mammutbaum": None,  # Sequoiadendron, Sequoia or Metasequoia
    "obstbaum": None,  # any fruit tree
}

# Longest names first, so "rosskastanie" wins over "kastanie" when searching free text.
_BY_LENGTH = sorted(COMMON_NAMES, key=len, reverse=True)


def genus_for_common_name(name: str | None) -> tuple[str | None, bool]:
    """``(genus, known)`` for a single common name; ``known`` is False for unmapped names."""
    key = (name or "").strip().lower()
    if key not in COMMON_NAMES:
        return None, False
    return COMMON_NAMES[key], True


def genera_in_text(text: str | None) -> set[str | None]:
    """All genera mentioned in free text such as '2 Rosskastanien, 1 Blutbuche'.

    Ambiguous names add ``None``. German compounds end with their base noun, so a
    name also matches at the end of a word, with plural endings: "Zerreichen",
    "Waldkiefer", "Kopfweiden". Longer names are tried first, so "Esskastanie"
    wins over "Kastanie" and "Sicheltanne" over "Tanne".
    """
    found: set[str | None] = set()
    rest = (text or "").lower()
    for name in _BY_LENGTH:
        pattern = rf"{re.escape(name)}(?:n|en|e|s)?(?![a-zäöüß])"
        if re.search(pattern, rest):
            found.add(COMMON_NAMES[name])
            rest = re.sub(pattern, " ", rest)
    return found
