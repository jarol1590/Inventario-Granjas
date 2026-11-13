-- ============================================================================
-- Migración del esquema v2.2 (PRAGMA user_version = 2) al v2.3 (user_version = 3).
-- Programas generalizados (valorización biológica solo en bovinos, categorías "General" y tarifas de referencia) y módulo de lotes.
-- Nunca borra datos: LOTE_POBLACION se recrea porque en las versiones anteriores ninguna pantalla la usaba (siempre estuvo vacía).
-- ============================================================================

ALTER TABLE PROGRAMA ADD COLUMN usa_valorizacion_biologica BOOLEAN NOT NULL DEFAULT 0;
ALTER TABLE PROGRAMA ADD COLUMN unidad_venta_default TEXT NOT NULL DEFAULT 'UNIDAD' CHECK (unidad_venta_default IN ('UNIDAD', 'KG'));
UPDATE PROGRAMA SET usa_valorizacion_biologica = 1 WHERE codigo = 'PRG-GAN';
UPDATE PROGRAMA SET unidad_venta_default = 'KG' WHERE codigo = 'PRG-AVE';

ALTER TABLE REMISION_DETALLE ADD COLUMN peso_vendido_kg DECIMAL(12,2);
ALTER TABLE REMISION_DETALLE ADD COLUMN cantidad INTEGER NOT NULL DEFAULT 1;
ALTER TABLE MOVIMIENTO_INVENTARIO ADD COLUMN cantidad INTEGER NOT NULL DEFAULT 1;

DROP TABLE LOTE_POBLACION;
CREATE TABLE LOTE_POBLACION (
    id                         INTEGER PRIMARY KEY AUTOINCREMENT,
    codigo_lote                TEXT NOT NULL UNIQUE COLLATE NOCASE,
    programa_id                INTEGER NOT NULL REFERENCES PROGRAMA(id),
    granja_id                  INTEGER NOT NULL REFERENCES GRANJA(id),
    etapa_id                   INTEGER NOT NULL REFERENCES CATEGORIA_ETAPA(id),
    clase_entrada              TEXT NOT NULL CHECK (clase_entrada IN ('COMPRA', 'NACIMIENTO', 'TRASLADO', 'DESDOBLE')),
    ubicacion                  TEXT NOT NULL,
    linea_raza                 TEXT NOT NULL,
    origen_referencia          TEXT,
    proveedor                  TEXT,
    orden_compra               TEXT,
    fecha_nacimiento_ingreso   DATETIME NOT NULL,
    control_numerico_poblacion BOOLEAN NOT NULL DEFAULT 1,
    cantidad_inicial           INTEGER NOT NULL,
    cantidad_actual            INTEGER NOT NULL,
    mortandad_acumulada        INTEGER NOT NULL DEFAULT 0,
    valor_compra_unitario      DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    valor_contable_unitario    DECIMAL(12,2) NOT NULL,
    valor_total_contable       DECIMAL(12,2) NOT NULL,
    observaciones              TEXT,
    estado                     TEXT NOT NULL DEFAULT 'Activo' CHECK (estado IN ('Activo', 'Finalizado'))
);

CREATE TABLE TARIFA_CATEGORIA_PROGRAMA (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    programa_id      INTEGER NOT NULL REFERENCES PROGRAMA(id),
    categoria_id     INTEGER NOT NULL REFERENCES CATEGORIA_ETAPA(id),
    nombre_categoria TEXT    NOT NULL COLLATE NOCASE,
    unidad_cobro     TEXT    NOT NULL DEFAULT 'UNIDAD' CHECK (unidad_cobro IN ('UNIDAD', 'KG')),
    valor_referencia DECIMAL(12,2) NOT NULL DEFAULT 0.00 CHECK (valor_referencia >= 0),
    es_default       BOOLEAN NOT NULL DEFAULT 0,
    activo           BOOLEAN NOT NULL DEFAULT 1,
    UNIQUE (programa_id, nombre_categoria)
);

-- Cada programa sin valorización biológica recibe su categoría "General" (si no la tenía) y su tarifa por defecto.
INSERT INTO CATEGORIA_ETAPA (nombre, sexo, programa_id)
SELECT 'General', 'Ambos', p.id FROM PROGRAMA p
WHERE p.usa_valorizacion_biologica = 0
  AND NOT EXISTS (SELECT 1 FROM CATEGORIA_ETAPA c WHERE c.programa_id = p.id AND c.nombre = 'General');

INSERT INTO TARIFA_CATEGORIA_PROGRAMA (programa_id, categoria_id, nombre_categoria, unidad_cobro, valor_referencia, es_default)
SELECT p.id, c.id, 'General', p.unidad_venta_default, 0, 1
FROM PROGRAMA p JOIN CATEGORIA_ETAPA c ON c.programa_id = p.id AND c.nombre = 'General'
WHERE p.usa_valorizacion_biologica = 0;