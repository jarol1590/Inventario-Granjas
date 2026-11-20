-- ============================================================================
-- v2.8 (parte 2): REMISION_DETALLE admite líneas de producto (hortalizas, huevos, leche...) además de animal y lote.
-- El CHECK «animal XOR lote» no se puede modificar: se reconstruye la tabla conservando todos los datos.
-- Se aplica solo si la tabla todavía no tiene la columna producto_id (lo decide DatabaseInitializer).
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
    CHECK ((animal_id IS NOT NULL) + (lote_id IS NOT NULL) + (producto_id IS NOT NULL) = 1)
);

INSERT INTO REMISION_DETALLE_NUEVA
    (id, remision_id, animal_id, lote_id, peso_momento_kg, valor_unitario_transaccion, valor_total_transaccion, costo_sistema_libros,
     observaciones_especificas, peso_vendido_kg, cantidad, lote_destino_id, orientacion_origen_id, orientacion_destino_id)
SELECT id, remision_id, animal_id, lote_id, peso_momento_kg, valor_unitario_transaccion, valor_total_transaccion, costo_sistema_libros,
       observaciones_especificas, peso_vendido_kg, cantidad, lote_destino_id, orientacion_origen_id, orientacion_destino_id
FROM REMISION_DETALLE;

DROP TABLE REMISION_DETALLE;
ALTER TABLE REMISION_DETALLE_NUEVA RENAME TO REMISION_DETALLE;

CREATE INDEX ix_detalle_remision ON REMISION_DETALLE(remision_id);
CREATE INDEX ix_detalle_animal   ON REMISION_DETALLE(animal_id);
