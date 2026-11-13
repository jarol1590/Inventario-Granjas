-- ============================================================================
-- Datos semilla iniciales (se ejecutan una sola vez, al crear la base).
-- Fuentes: documento v2.0, tabla de precios 2026, notas.docx y mockups de Configuración.
-- No se siembra CONFIGURACION_SISTEMA: su ausencia indica el primer arranque (crear contraseña).
-- ============================================================================

-- Orientaciones: solo existen Lácteo y Carne/Levante (no hay "Doble Propósito").
INSERT INTO ORIENTACION_PRODUCTIVA (nombre) VALUES ('Lácteo'), ('Carne/Levante');

-- Solo la ganadería bovina usa la valorización biológica mensual (AgingEngine); los demás programas usan valores de referencia.
INSERT INTO PROGRAMA (codigo, nombre, tipo_inventario, observaciones, usa_valorizacion_biologica, unidad_venta_default) VALUES
    ('PRG-GAN', 'Ganadería (Bovinos)', 'INDIVIDUAL', 'Control por semoviente (chapeta)', 1, 'UNIDAD'),
    ('PRG-AVE', 'Avicultura (Aves)',   'LOTE',       'Control por galpón',               0, 'KG');

-- Granjas. La orientación sugerida sirve para proponerla en los traslados:
-- Tesorito = leche, Montelindo = carne, La Cruz = hato mixto (sin sugerencia).
INSERT INTO GRANJA (codigo, nombre, activa, observaciones, orientacion_id) VALUES
    ('G-01', 'Tesorito',   1, 'Sede principal - ganado de leche',
        (SELECT id FROM ORIENTACION_PRODUCTIVA WHERE nombre = 'Lácteo')),
    ('G-02', 'La Cruz',    1, 'Hato mixto (leche y carne)', NULL),
    ('G-03', 'Montelindo', 1, 'Ganado de carne y avicultura',
        (SELECT id FROM ORIENTACION_PRODUCTIVA WHERE nombre = 'Carne/Levante'));

-- Ganadería en las tres sedes; avicultura solo en Montelindo.
INSERT INTO GRANJA_PROGRAMA (granja_id, programa_id)
SELECT g.id, p.id FROM GRANJA g, PROGRAMA p WHERE p.codigo = 'PRG-GAN';

INSERT INTO GRANJA_PROGRAMA (granja_id, programa_id)
SELECT g.id, p.id FROM GRANJA g, PROGRAMA p WHERE g.codigo = 'G-03' AND p.codigo = 'PRG-AVE';

-- Categorías biológicas del programa de ganadería.
INSERT INTO CATEGORIA_ETAPA (nombre, sexo, programa_id)
SELECT c.nombre, c.sexo, p.id
FROM PROGRAMA p
JOIN (
    SELECT 'Terneros'        AS nombre, 'Ambos'  AS sexo
    UNION ALL SELECT 'Levante',         'Ambos'
    UNION ALL SELECT 'Novilla Vientre', 'Hembra'
    UNION ALL SELECT 'Vacas General',   'Hembra'
    UNION ALL SELECT 'Reproductor',     'Macho'
    UNION ALL SELECT 'Sin clasificar',  'Ambos'
) c
WHERE p.codigo = 'PRG-GAN';

-- Vigencia 2026 (activa) con la tabla de precios oficial.
INSERT INTO VIGENCIA_VALORIZACION (anio, activa, observaciones)
VALUES (2026, 1, 'Tabla de precios oficial 2026');

INSERT INTO TABLA_VALORIZACION_BASE
    (vigencia_id, categoria_id, orientacion_id, edad_minima_meses, edad_maxima_meses, sexo, requiere_partos, valor_estandar)
SELECT v.id,
       (SELECT c.id FROM CATEGORIA_ETAPA c JOIN PROGRAMA p ON p.id = c.programa_id
         WHERE p.codigo = 'PRG-GAN' AND c.nombre = t.categoria),
       (SELECT o.id FROM ORIENTACION_PRODUCTIVA o WHERE o.nombre = t.orientacion),
       t.edad_min, t.edad_max, t.sexo, t.requiere_partos, t.valor
FROM VIGENCIA_VALORIZACION v
JOIN (
    SELECT 'Terneros' AS categoria, NULL AS orientacion, 0 AS edad_min, 2 AS edad_max, 'Hembra' AS sexo, 0 AS requiere_partos, 150000 AS valor
    UNION ALL SELECT 'Terneros',        NULL,            0,   2, 'Macho',  0,  160000
    UNION ALL SELECT 'Terneros',        NULL,            3,   7, 'Ambos',  0,  800000
    UNION ALL SELECT 'Levante',         NULL,            8,  11, 'Ambos',  0, 1200000
    -- Machos de 12 a 17 meses (hasta que son Reproductor): siguen clasificados como Levante, con su mismo valor.
    UNION ALL SELECT 'Levante',         NULL,           12,  17, 'Macho',  0, 1200000
    -- Novillas: siguen siendo Novilla Vientre mientras no tengan partos (con parto, desde los 24 meses, pasan a Vacas).
    UNION ALL SELECT 'Novilla Vientre', 'Lácteo',        12, 999, 'Hembra', 0, 3200000
    UNION ALL SELECT 'Novilla Vientre', 'Carne/Levante', 12, 999, 'Hembra', 0, 3200000
    UNION ALL SELECT 'Vacas General',   'Lácteo',        24, 999, 'Hembra', 1, 6000000
    UNION ALL SELECT 'Vacas General',   'Carne/Levante', 24, 999, 'Hembra', 1, 4000000
    UNION ALL SELECT 'Reproductor',     NULL,            18, 999, 'Macho',  0, 7000000
) t
WHERE v.anio = 2026;

-- Firma: valores de relleno. El nombre real de la autoridad se configura en Configuración > Firmas e Identidad.
INSERT INTO CONFIGURACION_FIRMA (nombre_autoridad, cargo_autoridad, entidad_facultad, telefono, ruta_imagen_firma)
VALUES ('NOMBRE DE LA AUTORIDAD', 'DECANO(A)', 'FACULTAD DE CIENCIAS AGROPECUARIAS', NULL, NULL);

-- Categoría "General" de los programas que no usan valorización biológica: permite registrar inventario sin configurar tarifas.
INSERT INTO CATEGORIA_ETAPA (nombre, sexo, programa_id)
SELECT 'General', 'Ambos', p.id FROM PROGRAMA p WHERE p.usa_valorizacion_biologica = 0;

INSERT INTO TARIFA_CATEGORIA_PROGRAMA (programa_id, categoria_id, nombre_categoria, unidad_cobro, valor_referencia, es_default)
SELECT p.id, c.id, 'General', p.unidad_venta_default, 0, 1
FROM PROGRAMA p JOIN CATEGORIA_ETAPA c ON c.programa_id = p.id AND c.nombre = 'General'
WHERE p.usa_valorizacion_biologica = 0;
