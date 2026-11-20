-- ============================================================================
-- Migración del esquema v2.6 (PRAGMA user_version = 6) al v2.7 (user_version = 7).
--  · ANIMAL.observaciones: texto libre editable en la ficha; sale en los informes de inventario.
--  · MOVIMIENTO_INVENTARIO.observaciones: motivo del asiento (p. ej. valorización de un lote por cambio de etapa).
--  · Índice por lote para el Kardex mensual de lotes.
-- ============================================================================

ALTER TABLE ANIMAL ADD COLUMN observaciones TEXT;
ALTER TABLE MOVIMIENTO_INVENTARIO ADD COLUMN observaciones TEXT;
CREATE INDEX IF NOT EXISTS ix_movimiento_lote ON MOVIMIENTO_INVENTARIO(lote_id);
