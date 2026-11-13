-- ============================================================================
-- Migración del esquema v2.3 (PRAGMA user_version = 3) al v2.4 (user_version = 4).
-- Documentos de lotes (venta y traslado), edición y anulación de la compra de un lote. Solo agrega columnas.
-- ============================================================================

-- Cuenta las operaciones hechas sobre el lote (etapa, desdoble, baja, venta, traslado): mientras sea 0 se pueden
-- corregir la cantidad y el precio de la compra.
ALTER TABLE LOTE_POBLACION ADD COLUMN operaciones INTEGER NOT NULL DEFAULT 0;
ALTER TABLE LOTE_POBLACION ADD COLUMN anulado BOOLEAN NOT NULL DEFAULT 0;

-- Lotes que ya tuvieron operaciones antes de esta versión.
UPDATE LOTE_POBLACION SET operaciones = 1
WHERE mortandad_acumulada > 0 OR clase_entrada = 'DESDOBLE'
   OR observaciones LIKE '%Cambio de etapa%' OR observaciones LIKE '%Desdoble%' OR cantidad_actual <> cantidad_inicial;

ALTER TABLE REMISION_DETALLE ADD COLUMN lote_destino_id INTEGER REFERENCES LOTE_POBLACION(id);