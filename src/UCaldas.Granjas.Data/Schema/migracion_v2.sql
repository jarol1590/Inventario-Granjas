-- ============================================================================
-- Migración del esquema v2.1 (PRAGMA user_version = 1) al v2.2 (user_version = 2).
-- Se ejecuta una sola vez sobre bases ya creadas; las bases nuevas nacen directamente en v2.2 (schema.sql + seed.sql).
-- Nunca borra datos y no pisa lo que el usuario haya cambiado.
-- ============================================================================

-- 1) Recuperación de contraseña (100 % local): pregunta secreta, y los hashes de su respuesta y de la clave de recuperación.
--    En las bases existentes quedan vacías hasta que el usuario configure la recuperación.
ALTER TABLE CONFIGURACION_SISTEMA ADD COLUMN pregunta_secreta TEXT;
ALTER TABLE CONFIGURACION_SISTEMA ADD COLUMN respuesta_hash TEXT;
ALTER TABLE CONFIGURACION_SISTEMA ADD COLUMN recovery_key_hash TEXT;

-- 2) Huecos de la matriz 2026. Cada cambio solo se aplica si la fila sigue tal como la sembró el sistema.

-- 2a) Novillas (hembras sin partos): siguen siendo "Novilla Vientre" después de los 24 meses (la fila pasa de 12–24 a 12–999).
UPDATE TABLA_VALORIZACION_BASE
SET edad_maxima_meses = 999
WHERE vigencia_id = (SELECT id FROM VIGENCIA_VALORIZACION WHERE anio = 2026)
  AND categoria_id = (SELECT c.id FROM CATEGORIA_ETAPA c JOIN PROGRAMA p ON p.id = c.programa_id
                       WHERE p.codigo = 'PRG-GAN' AND c.nombre = 'Novilla Vientre')
  AND sexo = 'Hembra' AND requiere_partos = 0
  AND edad_minima_meses = 12 AND edad_maxima_meses = 24;

-- 2b) Machos de 12 a 17 meses: siguen clasificados como "Levante" con el mismo valor de Levante. No se agrega si ya
--     existe una fila de machos (o de ambos sexos) que cubra alguna edad de ese rango.
INSERT INTO TABLA_VALORIZACION_BASE
    (vigencia_id, categoria_id, orientacion_id, edad_minima_meses, edad_maxima_meses, sexo, requiere_partos, valor_estandar)
SELECT v.id, c.id, NULL, 12, 17, 'Macho', 0,
       COALESCE((SELECT t.valor_estandar FROM TABLA_VALORIZACION_BASE t
                  WHERE t.vigencia_id = v.id AND t.categoria_id = c.id ORDER BY t.id LIMIT 1), 1200000)
FROM VIGENCIA_VALORIZACION v
JOIN PROGRAMA p ON p.codigo = 'PRG-GAN'
JOIN CATEGORIA_ETAPA c ON c.programa_id = p.id AND c.nombre = 'Levante'
WHERE v.anio = 2026
  AND NOT EXISTS (
        SELECT 1 FROM TABLA_VALORIZACION_BASE t
        WHERE t.vigencia_id = v.id
          AND t.sexo IN ('Macho', 'Ambos')
          AND t.requiere_partos = 0
          AND t.edad_minima_meses <= 17
          AND t.edad_maxima_meses >= 12);
