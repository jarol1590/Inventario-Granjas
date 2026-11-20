-- ============================================================================
-- Migración del esquema v2.7 (PRAGMA user_version = 7) al v2.8 (user_version = 8): módulo Remisiones.
--  · PROGRAMA.maneja_inventario_animal: 1 = bovinos, aves...; 0 = programas de productos (hortalizas, frutales...).
--  · OFICIO_VENTA.cerrado / fecha_cierre: un oficio cerrado no admite remisiones nuevas.
--  · REMISION: programa, comprador (convenio o cliente) y totales con descuento.
--  · Tablas nuevas: CONVENIO, CLIENTE, CATALOGO_PRODUCTO, CONFIGURACION_REMISIONES y RECIBO_CAJA (1:1 con la remisión).
--  · Las columnas de REMISION_DETALLE para productos las agrega migracion_v8_detalle.sql (reconstrucción de la tabla).
-- Las columnas agregadas no llevan FOREIGN KEY: SQLite no permite quitarlas después si la llevan.
-- ============================================================================

ALTER TABLE PROGRAMA ADD COLUMN maneja_inventario_animal BOOLEAN NOT NULL DEFAULT 1;

ALTER TABLE OFICIO_VENTA ADD COLUMN cerrado BOOLEAN NOT NULL DEFAULT 0;
ALTER TABLE OFICIO_VENTA ADD COLUMN fecha_cierre DATETIME;

ALTER TABLE REMISION ADD COLUMN programa_id INTEGER;
ALTER TABLE REMISION ADD COLUMN tipo_comprador TEXT CHECK (tipo_comprador IS NULL OR tipo_comprador IN ('CONVENIO', 'OTRO'));
ALTER TABLE REMISION ADD COLUMN convenio_id INTEGER;
ALTER TABLE REMISION ADD COLUMN cliente_id INTEGER;
ALTER TABLE REMISION ADD COLUMN subtotal DECIMAL(12,2) NOT NULL DEFAULT 0;
ALTER TABLE REMISION ADD COLUMN descuento_porcentaje DECIMAL(5,2) NOT NULL DEFAULT 0;
ALTER TABLE REMISION ADD COLUMN valor_descuento DECIMAL(12,2) NOT NULL DEFAULT 0;
ALTER TABLE REMISION ADD COLUMN total DECIMAL(12,2) NOT NULL DEFAULT 0;

-- Las remisiones anteriores no tenían totales: se completan con lo que facturaron sus líneas (pesos enteros).
UPDATE REMISION
SET subtotal = COALESCE((SELECT SUM(d.valor_total_transaccion) FROM REMISION_DETALLE d WHERE d.remision_id = REMISION.id), 0),
    total    = COALESCE((SELECT SUM(d.valor_total_transaccion) FROM REMISION_DETALLE d WHERE d.remision_id = REMISION.id), 0);

CREATE TABLE IF NOT EXISTS CONVENIO (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    nombre             TEXT NOT NULL,
    nit                TEXT NOT NULL UNIQUE COLLATE NOCASE,
    direccion          TEXT NOT NULL,
    telefono           TEXT NOT NULL,
    correo_electronico TEXT,
    activo             BOOLEAN NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS CLIENTE (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    nombre_razon_social TEXT NOT NULL,
    nit_cc              TEXT NOT NULL UNIQUE COLLATE NOCASE,
    direccion           TEXT,
    telefono            TEXT,
    correo_electronico  TEXT,
    activo              BOOLEAN NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS CATALOGO_PRODUCTO (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    codigo          TEXT NOT NULL UNIQUE COLLATE NOCASE,
    programa_id     INTEGER NOT NULL REFERENCES PROGRAMA(id),
    nombre          TEXT NOT NULL,
    unidad_medida   TEXT NOT NULL,
    precio_unitario DECIMAL(12,2) NOT NULL DEFAULT 0,
    activo          BOOLEAN NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS CONFIGURACION_REMISIONES (
    id                        INTEGER PRIMARY KEY CHECK (id = 1),
    nombre_director           TEXT NOT NULL DEFAULT '',
    cargo_director            TEXT NOT NULL DEFAULT '',
    titulo_director           TEXT,
    jefe_tesoreria            TEXT NOT NULL DEFAULT '',
    facultad_depto            TEXT NOT NULL DEFAULT '',
    telefonos                 TEXT,
    extensiones               TEXT,
    entidad_bancaria          TEXT NOT NULL DEFAULT 'DAVIVIENDA',
    tipo_cuenta               TEXT NOT NULL DEFAULT 'AHORROS',
    numero_cuenta             TEXT NOT NULL DEFAULT '084500007533',
    ultimo_consecutivo_recibo INTEGER NOT NULL DEFAULT 828
);

INSERT OR IGNORE INTO CONFIGURACION_REMISIONES (id, facultad_depto)
VALUES (1, 'Facultad de Ciencias Agropecuarias');

-- Recibo de Caja SP: exactamente uno por remisión (venta o traslado). Un recibo anulado conserva su número.
CREATE TABLE IF NOT EXISTS RECIBO_CAJA (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    numero_recibo     INTEGER NOT NULL UNIQUE,
    prefijo           TEXT NOT NULL DEFAULT 'SP',
    remision_id       INTEGER NOT NULL UNIQUE REFERENCES REMISION(id),
    fecha             DATETIME NOT NULL,
    forma_pago        TEXT NOT NULL CHECK (forma_pago IN ('CONSIGNACION', 'EFECTIVO_CHEQUE', 'TRASLADO_INTERNO')),
    banco_consignacion TEXT,
    es_efectivo       BOOLEAN NOT NULL DEFAULT 0,
    cheque1_num      TEXT,
    cheque2_num      TEXT,
    total_pagado      DECIMAL(12,2) NOT NULL,
    total_letras      TEXT NOT NULL,
    observaciones     TEXT,
    anulado           BOOLEAN NOT NULL DEFAULT 0
);
