-- ============================================================================
-- v2.12: el Recibo de Caja guarda la cuenta de la consignación o transferencia: número y tipo de cuenta
-- (la entidad sigue en banco_consignacion). Los recibos anteriores quedan con NULL y se completan al editarlos.
-- ============================================================================

ALTER TABLE RECIBO_CAJA ADD COLUMN numero_cuenta TEXT;
ALTER TABLE RECIBO_CAJA ADD COLUMN tipo_cuenta TEXT;
