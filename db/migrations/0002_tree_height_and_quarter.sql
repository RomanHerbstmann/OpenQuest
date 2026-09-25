-- Tree attributes added by enrichment: quarter (Stadtteil, from the city's
-- quarter polygons) and height_m (nDOM50 surface model of Geobasis NRW).

UPDATE asset_type
SET attribute_schema = jsonb_set(
        jsonb_set(
            attribute_schema,
            '{properties,quarter}',
            '{ "type": ["string", "null"], "description": "Stadtteil (statistical district)" }'
        ),
        '{properties,height_m}',
        '{ "type": ["number", "null"], "description": "Object height above ground at the tree point in metres (nDOM, 95th percentile within 2.5 m); not a measured tree height" }'
    )
WHERE key = 'tree';
