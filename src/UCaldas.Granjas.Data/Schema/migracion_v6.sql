-- ============================================================================
-- Migración del esquema v2.5 (PRAGMA user_version = 5) al v2.6 (user_version = 6).
-- Vacas General (con parto) pasa a valorizarse desde los 14 meses (antes 24): una novilla que tiene su primer parto
-- cambia de categoría al guardar la ficha. Solo se ajustan las filas que siguen en 24 meses (no pisa lo editado).
-- ============================================================================

UPDATE TABLA_VALORIZACION_BASE
SET edad_minima_meses = 14
WHERE requiere_partos = 1
  AND edad_minima_meses = 24
  AND categoria_id IN (SELECT c.id FROM CATEGORIA_ETAPA c JOIN PROGRAMA p ON p.id = c.programa_id
                       WHERE p.codigo = 'PRG-GAN' AND c.nombre = 'Vacas General');
