-- ============================================================================
-- Migración del esquema v2.4 (PRAGMA user_version = 4) al v2.5 (user_version = 5).
-- Catálogo dinámico de razas / líneas por programa. Solo agrega una tabla y datos; no toca los existentes.
-- ============================================================================

CREATE TABLE RAZA_LINEA_PROGRAMA (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    programa_id INTEGER NOT NULL REFERENCES PROGRAMA(id),
    nombre      TEXT    NOT NULL COLLATE NOCASE,
    es_default  BOOLEAN NOT NULL DEFAULT 0,
    activo      BOOLEAN NOT NULL DEFAULT 1,
    UNIQUE (programa_id, nombre)
);

-- Todo programa existente recibe la raza por defecto.
INSERT INTO RAZA_LINEA_PROGRAMA (programa_id, nombre, es_default)
SELECT id, 'No Especificada / Cruce', 1 FROM PROGRAMA;

-- Bovinos: las razas que antes estaban fijas en el código.
INSERT OR IGNORE INTO RAZA_LINEA_PROGRAMA (programa_id, nombre)
SELECT p.id, r.nombre
FROM PROGRAMA p,
     (SELECT 'Holstein' AS nombre UNION ALL SELECT 'Jersey' UNION ALL SELECT 'Normando' UNION ALL SELECT 'Pardo Suizo'
      UNION ALL SELECT 'Ayrshire' UNION ALL SELECT 'Gyr' UNION ALL SELECT 'Brahman' UNION ALL SELECT 'Cebú'
      UNION ALL SELECT 'Simmental' UNION ALL SELECT 'Angus' UNION ALL SELECT 'Romosinuano'
      UNION ALL SELECT 'Blanco Orejinegro (BON)' UNION ALL SELECT 'Criollo') r
WHERE p.usa_valorizacion_biologica = 1;

-- Aves: líneas genéticas comunes.
INSERT OR IGNORE INTO RAZA_LINEA_PROGRAMA (programa_id, nombre)
SELECT p.id, l.nombre
FROM PROGRAMA p,
     (SELECT 'Hy-Line Brown' AS nombre UNION ALL SELECT 'Ross' UNION ALL SELECT 'Cobb' UNION ALL SELECT 'Lohmann') l
WHERE p.codigo = 'PRG-AVE';

-- Lo que ya está registrado (animales y lotes) se conserva como opción de su programa.
INSERT OR IGNORE INTO RAZA_LINEA_PROGRAMA (programa_id, nombre)
SELECT DISTINCT c.programa_id, TRIM(a.raza)
FROM ANIMAL a JOIN CATEGORIA_ETAPA c ON c.id = a.categoria_id
WHERE TRIM(a.raza) <> '';

INSERT OR IGNORE INTO RAZA_LINEA_PROGRAMA (programa_id, nombre)
SELECT DISTINCT programa_id, TRIM(linea_raza)
FROM LOTE_POBLACION
WHERE TRIM(linea_raza) <> '';
