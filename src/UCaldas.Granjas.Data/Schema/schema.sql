-- ============================================================================
-- Sistema Unificado de Gestión de Granjas - Universidad de Caldas
-- Esquema v2.1 = esquema definitivo v2.0 del documento de contexto + extensiones aprobadas.
-- Cada extensión está marcada con [v2.1]. Ver docs/esquema-v2.1.md.
-- Las claves foráneas se activan por conexión (Foreign Keys=True en la cadena de conexión).
-- Las columnas de texto únicas usan COLLATE NOCASE: "0940-26" y "0940-26" en otra capitalización son el mismo valor.
-- ============================================================================

-- CONFIGURACIÓN -------------------------------------------------------------

-- Fila única. Mientras no exista, la aplicación está en su primer arranque.
CREATE TABLE CONFIGURACION_SISTEMA (
    id                          INTEGER PRIMARY KEY AUTOINCREMENT,
    password_hash               TEXT    NOT NULL,
    backup_automatico_salir     BOOLEAN NOT NULL DEFAULT 1,
    ruta_backup_predeterminada  TEXT,
    ultimo_cierre_valorizacion  DATE,                               -- [v2.1] último fin de mes valorizado
    pregunta_secreta            TEXT,                               -- [v2.2] pregunta de recuperación (texto elegido por el usuario)
    respuesta_hash              TEXT,                               -- [v2.2] PBKDF2-SHA256 de la respuesta normalizada
    recovery_key_hash           TEXT                                -- [v2.2] PBKDF2-SHA256 de la clave de recuperación (16 caracteres)
);

-- Fila única con la autoridad que firma los informes.
CREATE TABLE CONFIGURACION_FIRMA (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    nombre_autoridad  TEXT NOT NULL,
    cargo_autoridad   TEXT NOT NULL,
    entidad_facultad  TEXT NOT NULL,
    telefono          TEXT,
    ruta_imagen_firma TEXT                                          -- ruta local o Base64 PNG/JPG
);

-- ESTRUCTURA ORGANIZACIONAL -------------------------------------------------

CREATE TABLE ORIENTACION_PRODUCTIVA (
    id     INTEGER PRIMARY KEY AUTOINCREMENT,
    nombre TEXT NOT NULL UNIQUE COLLATE NOCASE
);

CREATE TABLE GRANJA (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    codigo         TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    nombre         TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    activa         BOOLEAN NOT NULL DEFAULT 1,
    observaciones  TEXT,
    orientacion_id INTEGER REFERENCES ORIENTACION_PRODUCTIVA(id)    -- [v2.1] orientación predominante (sugerencia en traslados)
);

CREATE TABLE PROGRAMA (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    codigo          TEXT NOT NULL UNIQUE COLLATE NOCASE,
    nombre          TEXT NOT NULL,
    tipo_inventario TEXT NOT NULL CHECK (tipo_inventario IN ('INDIVIDUAL', 'LOTE')),
    observaciones   TEXT,                                           -- [v2.1] visible en el mockup de Granjas y Programas
    usa_valorizacion_biologica BOOLEAN NOT NULL DEFAULT 0,          -- [v2.3] 1 solo en Bovinos: valorización mensual por edad (AgingEngine)
    unidad_venta_default       TEXT NOT NULL DEFAULT 'UNIDAD' CHECK (unidad_venta_default IN ('UNIDAD', 'KG'))   -- [v2.3]
);

CREATE TABLE GRANJA_PROGRAMA (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    granja_id   INTEGER NOT NULL REFERENCES GRANJA(id),
    programa_id INTEGER NOT NULL REFERENCES PROGRAMA(id),
    UNIQUE (granja_id, programa_id)
);

CREATE TABLE CATEGORIA_ETAPA (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    nombre      TEXT    NOT NULL COLLATE NOCASE,
    sexo        TEXT    NOT NULL CHECK (sexo IN ('Macho', 'Hembra', 'Ambos')),
    programa_id INTEGER NOT NULL REFERENCES PROGRAMA(id),
    UNIQUE (programa_id, nombre)
);

-- [v2.3] Categorías y valores de referencia de los programas que NO usan valorización biológica (todo menos Bovinos).
-- Cada fila está ligada a su CATEGORIA_ETAPA (la que referencian animales y lotes); "General" (es_default) no se puede borrar.
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

-- [v2.5] Catálogo de razas (bovinos, equinos...) y líneas genéticas (aves...) por programa. ANIMAL.raza y LOTE_POBLACION.linea_raza
-- siguen siendo texto (sin FK): una raza inactivada no invalida lo ya registrado. "No Especificada / Cruce" (es_default) no se inactiva.
CREATE TABLE RAZA_LINEA_PROGRAMA (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    programa_id INTEGER NOT NULL REFERENCES PROGRAMA(id),
    nombre      TEXT    NOT NULL COLLATE NOCASE,
    es_default  BOOLEAN NOT NULL DEFAULT 0,
    activo      BOOLEAN NOT NULL DEFAULT 1,
    UNIQUE (programa_id, nombre)
);

-- MATRIZ DE VALORIZACIÓN ANUAL ----------------------------------------------

CREATE TABLE VIGENCIA_VALORIZACION (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    anio          INTEGER NOT NULL UNIQUE,
    activa        BOOLEAN NOT NULL DEFAULT 0,
    observaciones TEXT
);

-- Solo una vigencia puede estar activa a la vez.
CREATE UNIQUE INDEX ux_vigencia_activa ON VIGENCIA_VALORIZACION(activa) WHERE activa = 1;

CREATE TABLE TABLA_VALORIZACION_BASE (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    vigencia_id       INTEGER NOT NULL REFERENCES VIGENCIA_VALORIZACION(id),
    categoria_id      INTEGER NOT NULL REFERENCES CATEGORIA_ETAPA(id),
    orientacion_id    INTEGER REFERENCES ORIENTACION_PRODUCTIVA(id),   -- NULL = aplica a todas
    edad_minima_meses INTEGER NOT NULL CHECK (edad_minima_meses >= 0),
    edad_maxima_meses INTEGER NOT NULL,
    sexo              TEXT    NOT NULL CHECK (sexo IN ('Macho', 'Hembra', 'Ambos')),
    requiere_partos   BOOLEAN NOT NULL DEFAULT 0,
    valor_estandar    DECIMAL(12,2) NOT NULL CHECK (valor_estandar >= 0),
    CHECK (edad_maxima_meses >= edad_minima_meses)
);

CREATE INDEX ix_tarifa_vigencia ON TABLA_VALORIZACION_BASE(vigencia_id);

-- SEMOVIENTES E INVENTARIOS -------------------------------------------------

CREATE TABLE ANIMAL (
    id                    INTEGER PRIMARY KEY AUTOINCREMENT,
    chapeta_amarilla      TEXT NOT NULL UNIQUE COLLATE NOCASE,
    chapeta_roja          TEXT,
    nombre                TEXT,
    sexo                  TEXT NOT NULL CHECK (sexo IN ('Macho', 'Hembra')),
    fecha_nacimiento      DATETIME NOT NULL,
    fecha_compra          DATETIME,                                   -- NULL si nació en la granja
    clase_entrada         TEXT NOT NULL CHECK (clase_entrada IN ('NACIMIENTO', 'COMPRA', 'TRASLADO')),
    raza                  TEXT NOT NULL,
    madre_identificacion  TEXT,
    peso_actual_kg        DECIMAL(10,2) NOT NULL,
    orientacion_id        INTEGER NOT NULL REFERENCES ORIENTACION_PRODUCTIVA(id),
    valor_compra_origen   DECIMAL(12,2) NOT NULL DEFAULT 0.00,        -- inmutable
    valor_contable_actual DECIMAL(12,2) NOT NULL,                     -- dinámico (AgingEngine)
    granja_id             INTEGER NOT NULL REFERENCES GRANJA(id),
    categoria_id          INTEGER NOT NULL REFERENCES CATEGORIA_ETAPA(id),
    estado                TEXT NOT NULL DEFAULT 'Disponible'
                          CHECK (estado IN ('Disponible', 'Vendido', 'Hurtado', 'Muerto', 'Trasladado')),
    numero_partos         INTEGER NOT NULL DEFAULT 0 CHECK (numero_partos >= 0),   -- [v2.1]
    exento_valorizacion   BOOLEAN NOT NULL DEFAULT 0,                              -- [v2.1]
    fecha_registro        DATETIME NOT NULL DEFAULT (datetime('now', 'localtime')) -- [v2.1]
);

CREATE INDEX ix_animal_granja_estado ON ANIMAL(granja_id, estado);
CREATE INDEX ix_animal_categoria     ON ANIMAL(categoria_id);
CREATE INDEX ix_animal_madre         ON ANIMAL(madre_identificacion);

CREATE TABLE LOTE_POBLACION (                                                      -- [v2.3] ver especificacion_tecnica_modulo_lotes
    id                         INTEGER PRIMARY KEY AUTOINCREMENT,
    codigo_lote                TEXT NOT NULL UNIQUE COLLATE NOCASE,
    programa_id                INTEGER NOT NULL REFERENCES PROGRAMA(id),
    granja_id                  INTEGER NOT NULL REFERENCES GRANJA(id),
    etapa_id                   INTEGER NOT NULL REFERENCES CATEGORIA_ETAPA(id),
    clase_entrada              TEXT NOT NULL CHECK (clase_entrada IN ('COMPRA', 'NACIMIENTO', 'TRASLADO', 'DESDOBLE')),
    ubicacion                  TEXT NOT NULL,                                -- galpón, estanque, corral
    linea_raza                 TEXT NOT NULL,
    origen_referencia          TEXT,                                         -- N.º de factura, chapeta de la madre o lote padre (desdoble)
    proveedor                  TEXT,
    orden_compra               TEXT,
    fecha_nacimiento_ingreso   DATETIME NOT NULL,
    control_numerico_poblacion BOOLEAN NOT NULL DEFAULT 1,                   -- 0 = estanque sin conteo de individuos (piscicultura)
    cantidad_inicial           INTEGER NOT NULL,
    cantidad_actual            INTEGER NOT NULL,
    mortandad_acumulada        INTEGER NOT NULL DEFAULT 0,
    valor_compra_unitario      DECIMAL(12,2) NOT NULL DEFAULT 0.00,          -- inmutable ($0 si es nacimiento)
    valor_contable_unitario    DECIMAL(12,2) NOT NULL,                       -- dinámico por etapa
    valor_total_contable       DECIMAL(12,2) NOT NULL,
    observaciones              TEXT,
    estado                     TEXT NOT NULL DEFAULT 'Activo' CHECK (estado IN ('Activo', 'Finalizado')),
    operaciones                INTEGER NOT NULL DEFAULT 0,                   -- [v2.4] cambios de etapa, desdobles, bajas, ventas y traslados
    anulado                    BOOLEAN NOT NULL DEFAULT 0                    -- [v2.4] compra del lote anulada
);

CREATE TABLE DATOS_GENEALOGIA (
    id                        INTEGER PRIMARY KEY AUTOINCREMENT,
    animal_id                 INTEGER NOT NULL UNIQUE REFERENCES ANIMAL(id),
    es_inseminacion           BOOLEAN NOT NULL DEFAULT 0,
    raza_padre_inseminacion   TEXT,
    nombre_padre_inseminacion TEXT,
    padre_id                  INTEGER REFERENCES ANIMAL(id)
);

-- FLUJO DOCUMENTAL: OFICIOS, REMISIONES, MOVIMIENTOS ------------------------

CREATE TABLE OFICIO_VENTA (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    numero_oficio TEXT NOT NULL UNIQUE COLLATE NOCASE,                -- exclusivo de ventas (OF-120)
    programa_id   INTEGER NOT NULL REFERENCES PROGRAMA(id),
    fecha_oficio  DATETIME NOT NULL,
    observaciones TEXT
);

CREATE TABLE REMISION (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    numero_remision   TEXT NOT NULL UNIQUE COLLATE NOCASE,            -- en COMPRA es el número de factura / remisión del proveedor
    tipo_movimiento   TEXT NOT NULL CHECK (tipo_movimiento IN ('VENTA', 'TRASLADO', 'COMPRA', 'BAJA')),
    oficio_id         INTEGER REFERENCES OFICIO_VENTA(id),
    recibo_caja       TEXT,
    granja_origen_id  INTEGER REFERENCES GRANJA(id),
    granja_destino_id INTEGER REFERENCES GRANJA(id),
    fecha_remision    DATETIME NOT NULL,
    tercero_entidad   TEXT,                                           -- comprador o proveedor
    observaciones     TEXT,
    orden_compra      TEXT,                                           -- [v2.1] solo COMPRA
    tercero_nit       TEXT,                                           -- [v2.1] solo COMPRA
    clase_salida      TEXT CHECK (clase_salida IS NULL OR clase_salida IN
                          ('VENTA', 'MUERTE', 'HURTO', 'DESCARTE_SANITARIO', 'TRASLADO')),   -- [v2.1]
    anulada           BOOLEAN NOT NULL DEFAULT 0,                                            -- [v2.1]
    -- Jerarquía documental de 3 niveles (3.4 del documento): la VENTA exige Oficio y Recibo de Caja;
    -- traslados, bajas y compras los rechazan.
    CHECK (tipo_movimiento <> 'VENTA'
           OR (oficio_id IS NOT NULL AND recibo_caja IS NOT NULL AND length(trim(recibo_caja)) > 0)),
    CHECK (tipo_movimiento = 'VENTA' OR (oficio_id IS NULL AND recibo_caja IS NULL))
);

CREATE INDEX ix_remision_tipo_fecha ON REMISION(tipo_movimiento, fecha_remision);
CREATE INDEX ix_remision_oficio     ON REMISION(oficio_id);

CREATE TABLE REMISION_DETALLE (
    id                         INTEGER PRIMARY KEY AUTOINCREMENT,
    remision_id                INTEGER NOT NULL REFERENCES REMISION(id),
    animal_id                  INTEGER REFERENCES ANIMAL(id),
    lote_id                    INTEGER REFERENCES LOTE_POBLACION(id),
    peso_momento_kg            DECIMAL(10,2),
    valor_unitario_transaccion DECIMAL(12,2),
    valor_total_transaccion    DECIMAL(12,2),
    costo_sistema_libros       DECIMAL(12,2) NOT NULL,
    observaciones_especificas  TEXT,
    peso_vendido_kg            DECIMAL(12,2),                                   -- [v2.3] venta por kilogramo (báscula)
    cantidad                   INTEGER NOT NULL DEFAULT 1,                      -- [v2.3] 1 en animales; N en lotes
    lote_destino_id            INTEGER REFERENCES LOTE_POBLACION(id),           -- [v2.4] lote que creó un traslado parcial de lotes
    orientacion_origen_id      INTEGER REFERENCES ORIENTACION_PRODUCTIVA(id),   -- [v2.1]
    orientacion_destino_id     INTEGER REFERENCES ORIENTACION_PRODUCTIVA(id),   -- [v2.1]
    CHECK ((animal_id IS NOT NULL) + (lote_id IS NOT NULL) = 1)
);

CREATE INDEX ix_detalle_remision ON REMISION_DETALLE(remision_id);
CREATE INDEX ix_detalle_animal   ON REMISION_DETALLE(animal_id);

-- Libro del Kardex: una fila por animal y movimiento; costo_asociado lleva signo.
-- Exactamente una de las dos granjas está informada: la que el movimiento afecta.
CREATE TABLE MOVIMIENTO_INVENTARIO (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    animal_id         INTEGER REFERENCES ANIMAL(id),
    lote_id           INTEGER REFERENCES LOTE_POBLACION(id),
    remision_id       INTEGER REFERENCES REMISION(id),
    tipo_movimiento   TEXT NOT NULL CHECK (tipo_movimiento IN
                          ('Nacimiento', 'Compra', 'Venta', 'Muerte', 'Hurto', 'Traslado', 'Valorizacion')),
    fecha_movimiento  DATETIME NOT NULL,
    granja_origen_id  INTEGER REFERENCES GRANJA(id),
    granja_destino_id INTEGER REFERENCES GRANJA(id),
    costo_asociado    DECIMAL(12,2) NOT NULL,
    cantidad          INTEGER NOT NULL DEFAULT 1,                               -- [v2.3]
    CHECK ((granja_origen_id IS NOT NULL) + (granja_destino_id IS NOT NULL) = 1)
);

CREATE INDEX ix_movimiento_animal   ON MOVIMIENTO_INVENTARIO(animal_id);
CREATE INDEX ix_movimiento_fecha    ON MOVIMIENTO_INVENTARIO(fecha_movimiento);
CREATE INDEX ix_movimiento_remision ON MOVIMIENTO_INVENTARIO(remision_id);
