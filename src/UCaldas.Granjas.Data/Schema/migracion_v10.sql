-- ============================================================================
-- v2.10: el NIT o cédula de un cliente («Otro comprador») es opcional. nit_cc pasa a admitir NULL
-- (UNIQUE permite varios NULL). Ninguna tabla tiene llave foránea hacia CLIENTE, así que se reconstruye
-- conservando los datos y los ids.
-- ============================================================================

CREATE TABLE CLIENTE_NUEVA (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    nombre_razon_social TEXT NOT NULL,
    nit_cc              TEXT UNIQUE COLLATE NOCASE,
    direccion           TEXT,
    telefono            TEXT,
    correo_electronico  TEXT,
    activo              BOOLEAN NOT NULL DEFAULT 1
);

INSERT INTO CLIENTE_NUEVA (id, nombre_razon_social, nit_cc, direccion, telefono, correo_electronico, activo)
SELECT id, nombre_razon_social, nit_cc, direccion, telefono, correo_electronico, activo FROM CLIENTE;

DROP TABLE CLIENTE;
ALTER TABLE CLIENTE_NUEVA RENAME TO CLIENTE;
