-- ============================================================================
-- v2.9: las remisiones de subproductos admiten líneas escritas a mano (sin producto de catálogo).
-- El CHECK pasa a «una línea es un animal, un lote o un ítem descrito (con o sin producto)». Se reconstruye la tabla
-- conservando todos los datos; DatabaseInitializer la aplica solo si el CHECK todavía es el de la v2.8.
-- ============================================================================

CREATE TABLE REMISION_DETALLE_NUEVA (
    id                         INTEGER PRIMARY KEY AUTOINCREMENT,
    remision_id                INTEGER NOT NULL REFERENCES REMISION(id),
    animal_id                  INTEGER REFERENCES ANIMAL(id),
    lote_id                    INTEGER REFERENCES LOTE_POBLACION(id),
    peso_momento_kg            DECIMAL(10,2),
    valor_unitario_transaccion DECIMAL(12,2),
    valor_total_transaccion    DECIMAL(12,2),
    costo_sistema_libros       DECIMAL(12,2) NOT NULL,
    observaciones_especificas  TEXT,
    peso_vendido_kg            DECIMAL(12,2),
    cantidad                   INTEGER NOT NULL DEFAULT 1,
    lote_destino_id            INTEGER REFERENCES LOTE_POBLACION(id),
    orientacion_origen_id      INTEGER REFERENCES ORIENTACION_PRODUCTIVA(id),
    orientacion_destino_id     INTEGER REFERENCES ORIENTACION_PRODUCTIVA(id),
    producto_id                INTEGER REFERENCES CATALOGO_PRODUCTO(id),        -- [v2.8]
    descripcion_item           TEXT,                                            -- [v2.8]
    unidad_medida              TEXT,                                            -- [v2.8]
    cantidad_producto          DECIMAL(12,2),                                   -- [v2.8] (kg, cubetas...) en líneas de producto
    CHECK ((animal_id IS NOT NULL) + (lote_id IS NOT NULL) + (producto_id IS NOT NULL OR descripcion_item IS NOT NULL) = 1)
);

INSERT INTO REMISION_DETALLE_NUEVA SELECT * FROM REMISION_DETALLE;

DROP TABLE REMISION_DETALLE;
ALTER TABLE REMISION_DETALLE_NUEVA RENAME TO REMISION_DETALLE;

CREATE INDEX ix_detalle_remision ON REMISION_DETALLE(remision_id);
CREATE INDEX ix_detalle_animal   ON REMISION_DETALLE(animal_id);
