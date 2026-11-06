# DOCUMENTO DE CONTEXTO TÉCNICO Y PLAN DE DESARROLLO CON IA GENERATIVA
## SISTEMA UNIFICADO DE GESTIÓN DE GRANJAS — UNIVERSIDAD DE CALDAS
**Versión del Documento:** 2.0 (Fase de Inicio de Desarrollo - Beta MVP)  
**Tecnologías:** .NET 8 (WPF) | SQLite | MVVM Pattern | ClosedXML | LiveCharts2  

---

## 1. RESUMEN EJECUTIVO Y VISIÓN DEL PROYECTO

El **Sistema Unificado de Gestión de Granjas** es una solución de software de escritorio diseñada para la Facultad de Ciencias Agropecuarias de la Universidad de Caldas. Su objetivo principal es automatizar y asegurar el control operativo y contable de los semovientes (bovinos y lotes de producción) alojados en las diferentes sedes universitarias (*Tesorito*, *La Cruz*, *Montelindo*).

### Entorno de Operación
* **Operación Monousuario y Offline-First:** El sistema se ejecuta en computadores locales instalados en entornos rurales o administrativos sin dependencia de conexión a Internet ni servidores remotos.
* **Seguridad por Contraseña:** Acceso directo mediante PIN/Contraseña único (sin diferenciación de perfiles multiusuario).
* **Auditoría Financiera:** Generación de reportes contables oficiales exclusivamente en Microsoft Excel (`.xlsx`) con incrustación dinámica de datos y firmas manuscritas digitalizadas de las autoridades (ej. Decanatura).

---

## 2. ARQUITECTURA DEL SISTEMA Y STACK TECNOLÓGICO

El sistema adopta una **Arquitectura Monolítica en N-Capas (N-Tier) basada en el patrón MVVM (Model-View-ViewModel)**.

```
+-------------------------------------------------------------------------+
| CAPA DE PRESENTACIÓN (WPF / .NET 8 - XAML)                              |
| Views (Interfaces UI) + ViewModels + Converters + Navigation Service    |
+-------------------------------------------------------------------------+
                                   │
                                   ▼
+-------------------------------------------------------------------------+
| CAPA DE LÓGICA DE NEGOCIO Y SERVICIOS (C# Application Services)          |
| AgingEngine | KardexService | DocumentValidationService | ExcelReport   |
+-------------------------------------------------------------------------+
                                   │
                                   ▼
+-------------------------------------------------------------------------+
| CAPA DE ACCESO A DATOS (DAL / Repositorios)                             |
| Generic & Specific Repositories | SQLite Connection Factory | Dapper/EF |
+-------------------------------------------------------------------------+
                                   │
                                   ▼
+-------------------------------------------------------------------------+
| CAPA DE ALMACENAMIENTO (Base de Datos SQLite Local)                     |
| Archivo unificado: granjas_semovientes.db (Beta MVP)                    |
+-------------------------------------------------------------------------+
```

### Componentes Clave
* **Presentation Layer:** C# .NET 8 WPF, XAML estilizado, `CommunityToolkit.Mvvm` para binding bidireccional y comandos.
* **Business Services:**
  * `AgingEngine`: Recálculo de edades, categorización biológica y asignación de precios contables.
  * `DocumentValidationService`: Validación de la jerarquía de ventas y control anti-solapamiento de tarifas.
  * `ExcelReportingEngine`: Motor de exportación a Excel en `.xlsx` usando `ClosedXML` o `EPPlus`.
  * `BackupService`: Empaquetado comprimido `.zip` atómico de la base de datos con comandos `VACUUM`.
* **Data Layer:** SQLite local. En la versión Beta MVP, las tablas de semovientes, remisiones/ventas y configuración residen en una única base de datos (`granjas_semovientes.db`) para garantizar integridad referencial nativa.

---

## 3. REGLAS DE NEGOCIO Y MOTOR CONTABLE

### 3.1. Doble Sistema de Precios por Bovino
1. **Valor Compra Origen (`valor_compra_origen`):** Inmutable. Representa el costo real de adquisición en factura comercial. Si el animal nació en la granja (`clase_entrada = 'NACIMIENTO'`), su valor de compra origen es $0.
2. **Valor Contable Actual (`valor_contable_actual`):** Dinámico. Asignado por el `AgingEngine` según la matriz anual de tarifas de valorización por edad, sexo, partos y orientación productiva.

### 3.2. Motor de Envejecimiento (`AgingEngine`) y Regla de Desempate (Fallback)
* El motor calcula la edad en meses: $\text{EdadMeses} = \text{FechaActual} - \text{FechaNacimiento}$.
* Asigna la categoría biológica cruzando con `TABLA_VALORIZACION_BASE`.
* **Regla de Desempate por Especificidad:** Si existe coincidencia de rangos de edad, la prioridad se define por:
  1. Orientación Productiva específica (*Lácteo*, *Carne*) sobre la genérica (*Todas*).
  2. Menor `edad_minima_meses`.

### 3.3. Validación Anti-Solapamiento de Tarifas
Al guardar o modificar la matriz de valorización anual, el sistema ejecuta la formula de validación de rangos sobre filas del mismo sexo e historia productiva:
$$\text{Existe Solapamiento si: } \text{Max}(\text{Min}_A, \text{Min}_B) \le \text{Min}(\text{Max}_A, \text{Max}_B)$$
Si se detecta un solapamiento, se bloquea la persistencia y se muestra una alerta explicativa al usuario.

### 3.4. Jerarquía Documental de 3 Niveles para Ventas
Toda venta exige una cadena de validez estricta:
1. **Oficio de Venta (`OFICIO_VENTA`):** Documento formal asignado a un solo programa productivo (ej. *OF-120*).
2. **Remisión (`REMISION`):** Detalle de egreso de los animales amarrado al oficio.
3. **Recibo de Caja (`recibo_caja`):** Campo obligatorio en la remisión que acredita el recaudo financiero (ej. *RC-8841*).

*Nota:* Los traslados entre granjas y las bajas por enfermedad/hurto solo utilizan **Remisión** (sin Oficio ni Recibo de Caja).

---

## 4. ESQUEMA DE BASE DE DATOS DEFINITIVO (SQLITE)

```sql
-- TABLAS DE CONFIGURACIÓN Y ESTRUCTURA ORGANIZACIONAL
CREATE TABLE CONFIGURACION_SISTEMA (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    password_hash TEXT NOT NULL,
    backup_automatico_salir BOOLEAN DEFAULT 1,
    ruta_backup_predeterminada TEXT
);

CREATE TABLE CONFIGURACION_FIRMA (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    nombre_autoridad TEXT NOT NULL, -- Ej: PAULA VALENCIA ARANGO
    cargo_autoridad TEXT NOT NULL,  -- Ej: DECANO / DIR. GRANJAS
    entidad_facultad TEXT NOT NULL, -- Ej: FACULTAD DE CIENCIAS AGROPECUARIAS
    telefono TEXT,
    ruta_imagen_firma TEXT          -- Ruta local o Base64 PNG/JPG
);

CREATE TABLE GRANJA (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    codigo TEXT UNIQUE NOT NULL,    -- Ej: G-01, G-02
    nombre TEXT NOT NULL,           -- Ej: Tesorito, La Cruz, Montelindo
    activa BOOLEAN DEFAULT 1,
    observaciones TEXT
);

CREATE TABLE PROGRAMA (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    codigo TEXT UNIQUE NOT NULL,    -- Ej: PRG-GAN, PRG-AVE
    nombre TEXT NOT NULL,           -- Ej: Ganadería (Bovinos), Avícola (Aves)
    tipo_inventario TEXT NOT NULL   -- 'INDIVIDUAL' o 'LOTE'
);

CREATE TABLE GRANJA_PROGRAMA (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    granja_id INTEGER NOT NULL,
    programa_id INTEGER NOT NULL,
    FOREIGN KEY (granja_id) REFERENCES GRANJA(id),
    FOREIGN KEY (programa_id) REFERENCES PROGRAMA(id)
);

CREATE TABLE ORIENTACION_PRODUCTIVA (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    nombre TEXT NOT NULL            -- Lácteo, Carne/Levante, Doble Propósito
);

CREATE TABLE CATEGORIA_ETAPA (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    nombre TEXT NOT NULL,           -- Terneros, Levante, Novilla Vientre, Vacas General, Reproductor
    sexo TEXT NOT NULL,             -- Macho, Hembra, Ambos
    programa_id INTEGER NOT NULL,
    FOREIGN KEY (programa_id) REFERENCES PROGRAMA(id)
);

-- MATRIZ DE VALORIZACIÓN ANUAL
CREATE TABLE VIGENCIA_VALORIZACION (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    anio INTEGER UNIQUE NOT NULL,   -- Ej: 2026
    activa BOOLEAN DEFAULT 0,
    observaciones TEXT
);

CREATE TABLE TABLA_VALORIZACION_BASE (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    vigencia_id INTEGER NOT NULL,
    categoria_id INTEGER NOT NULL,
    orientacion_id INTEGER,         -- NULL aplica a todas
    edad_minima_meses INTEGER NOT NULL,
    edad_maxima_meses INTEGER NOT NULL,
    sexo TEXT NOT NULL,             -- Macho, Hembra, Ambos
    requiere_partos BOOLEAN DEFAULT 0,
    valor_estandar DECIMAL(12,2) NOT NULL,
    FOREIGN KEY (vigencia_id) REFERENCES VIGENCIA_VALORIZACION(id),
    FOREIGN KEY (categoria_id) REFERENCES CATEGORIA_ETAPA(id),
    FOREIGN KEY (orientacion_id) REFERENCES ORIENTACION_PRODUCTIVA(id)
);

-- CORE DE SEMOVIENTES E INVENTARIOS
CREATE TABLE ANIMAL (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    chapeta_amarilla TEXT UNIQUE NOT NULL,
    chapeta_roja TEXT,
    nombre TEXT,                     -- Alias o nombre individual
    sexo TEXT NOT NULL,
    fecha_nacimiento DATETIME NOT NULL,
    fecha_compra DATETIME,           -- NULL si nació en granja
    clase_entrada TEXT NOT NULL,     -- 'NACIMIENTO', 'COMPRA', 'TRASLADO'
    raza TEXT NOT NULL,
    madre_identificacion TEXT,
    peso_actual_kg DECIMAL(10,2) NOT NULL,
    orientacion_id INTEGER NOT NULL, -- Orientación exclusiva del semoviente
    valor_compra_origen DECIMAL(12,2) DEFAULT 0.00, -- Inmutable
    valor_contable_actual DECIMAL(12,2) NOT NULL,   -- Dinámico por AgingEngine
    granja_id INTEGER NOT NULL,
    categoria_id INTEGER NOT NULL,
    estado TEXT NOT NULL DEFAULT 'Disponible', -- Disponible, Vendido, Hurtado, Muerto, Trasladado
    FOREIGN KEY (orientacion_id) REFERENCES ORIENTACION_PRODUCTIVA(id),
    FOREIGN KEY (granja_id) REFERENCES GRANJA(id),
    FOREIGN KEY (categoria_id) REFERENCES CATEGORIA_ETAPA(id)
);

CREATE TABLE LOTE_POBLACION (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    codigo_lote TEXT UNIQUE NOT NULL,
    programa_id INTEGER NOT NULL,
    granja_id INTEGER NOT NULL,
    ubicacion TEXT NOT NULL,         -- Galpón, Estanque, Era
    cantidad_inicial INTEGER NOT NULL,
    cantidad_actual INTEGER NOT NULL,
    mortandad_acumulada INTEGER DEFAULT 0,
    valor_unitario DECIMAL(12,2) NOT NULL,
    valor_total DECIMAL(12,2) NOT NULL,
    fecha_ingreso DATETIME NOT NULL,
    estado TEXT DEFAULT 'Activo',
    FOREIGN KEY (programa_id) REFERENCES PROGRAMA(id),
    FOREIGN KEY (granja_id) REFERENCES GRANJA(id)
);

CREATE TABLE DATOS_GENEALOGIA (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    animal_id INTEGER UNIQUE NOT NULL,
    es_inseminacion BOOLEAN DEFAULT 0,
    raza_padre_inseminacion TEXT,
    nombre_padre_inseminacion TEXT,
    padre_id INTEGER,
    FOREIGN KEY (animal_id) REFERENCES ANIMAL(id),
    FOREIGN KEY (padre_id) REFERENCES ANIMAL(id)
);

-- FLUJO DOCUMENTAL DE EGRESOS, VENTAS Y TRASLADOS
CREATE TABLE OFICIO_VENTA (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    numero_oficio TEXT UNIQUE NOT NULL, -- Exclusivo para Ventas (ej: OF-120)
    programa_id INTEGER NOT NULL,
    fecha_oficio DATETIME NOT NULL,
    observaciones TEXT,
    FOREIGN KEY (programa_id) REFERENCES PROGRAMA(id)
);

CREATE TABLE REMISION (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    numero_remision TEXT UNIQUE NOT NULL, -- Universal (REM-001)
    tipo_movimiento TEXT NOT NULL,        -- 'VENTA', 'TRASLADO', 'COMPRA', 'BAJA'
    oficio_id INTEGER,                    -- Requerido SOLO si tipo_movimiento = 'VENTA'
    recibo_caja TEXT,                     -- Requerido SOLO si tipo_movimiento = 'VENTA'
    granja_origen_id INTEGER,
    granja_destino_id INTEGER,
    fecha_remision DATETIME NOT NULL,
    tercero_entidad TEXT,                 -- Comprador o Proveedor
    observaciones TEXT,
    FOREIGN KEY (oficio_id) REFERENCES OFICIO_VENTA(id),
    FOREIGN KEY (granja_origen_id) REFERENCES GRANJA(id),
    FOREIGN KEY (granja_destino_id) REFERENCES GRANJA(id)
);

CREATE TABLE REMISION_DETALLE (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    remision_id INTEGER NOT NULL,
    animal_id INTEGER,
    lote_id INTEGER,
    peso_momento_kg DECIMAL(10,2),
    valor_unitario_transaccion DECIMAL(12,2),
    valor_total_transaccion DECIMAL(12,2),
    costo_sistema_libros DECIMAL(12,2) NOT NULL,
    observaciones_especificas TEXT,
    FOREIGN KEY (remision_id) REFERENCES REMISION(id),
    FOREIGN KEY (animal_id) REFERENCES ANIMAL(id),
    FOREIGN KEY (lote_id) REFERENCES LOTE_POBLACION(id)
);

CREATE TABLE MOVIMIENTO_INVENTARIO (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    animal_id INTEGER,
    lote_id INTEGER,
    remision_id INTEGER,
    tipo_movimiento TEXT NOT NULL, -- Nacimiento, Compra, Venta, Muerte, Hurto, Traslado, Valorizacion
    fecha_movimiento DATETIME NOT NULL,
    granja_origen_id INTEGER,
    granja_destino_id INTEGER,
    costo_asociado DECIMAL(12,2) NOT NULL,
    FOREIGN KEY (animal_id) REFERENCES ANIMAL(id),
    FOREIGN KEY (lote_id) REFERENCES LOTE_POBLACION(id),
    FOREIGN KEY (remision_id) REFERENCES REMISION(id),
    FOREIGN KEY (granja_origen_id) REFERENCES GRANJA(id),
    FOREIGN KEY (granja_destino_id) REFERENCES GRANJA(id)
);
```

---

## 5. PLAN DE DESARROLLO CON IA GENERATIVA (PROMPT ENGINEERING STRATEGY)

Este plan guía el desarrollo interactivo paso a paso usando asistentes de Inteligencia Artificial (ChatGPT, Claude, Gemini, etc.).

### 5.1. Reglas Globales de Código para la IA (System Instructions)
Al iniciar cualquier sesión de codificación con la IA, proporciona estas directrices:

```text
Eres un Arquitecto Senior C# .NET 8 y WPF. Sigue estas reglas estrictas:
1. Aplica MVVM rigurosamente usando CommunityToolkit.Mvvm (ObservableProperty, RelayCommand).
2. Cero lógica de negocio o acceso a datos en el Code-Behind (Views .xaml.cs).
3. Todas las operaciones I/O y de SQLite deben ser asíncronas (async/await).
4. Aplica el patrón Repositorio y Servicio. Inyecta dependencias mediante Microsoft.Extensions.DependencyInjection.
5. El código debe ser limpio, con manejo defensivo de excepciones y validaciones claras.
6. Cumple al 100% las reglas de negocio descritas en el contexto (Doble precio, Jerarquía Oficio-Remisión-Recibo, Firma en Excel).
```

---

### 5.2. Mapa Sequencial de Prompts de Desarrollo (Fase 1: Beta MVP)

```
PASO 1: Estructura del Proyecto & SQLite DbContext
   │
   ▼
PASO 2: Repositorios y Entidades de Dominio
   │
   ▼
PASO 3: Servicios de Lógica de Negocio (AgingEngine, Validation, Excel)
   │
   ▼
PASO 4: ViewModels (Bovinos, Compras, Ventas, Traslados, Configuración)
   │
   ▼
PASO 5: Vistas WPF (XAML, Layouts, Estilos, DataTemplates)
```

---

#### 📋 PROMPT PASO 1: Infraestructura y Base de Datos (SQLite + Dapper / EF Core)

> **Prompt a copiar y enviar a la IA:**
> ```text
> Actúa como Desarrollador Backend C#. Necesito crear la infraestructura inicial para la versión Beta del Sistema Granjas de la Universidad de Caldas en .NET 8.
> 
> Tarea:
> 1. Genera las clases de Entidad C# que mapeen exactamente las tablas de SQLite expuestas en la Sección 4 del contexto: ANIMAL, GRANJA, PROGRAMA, VIGENCIA_VALORIZACION, TABLA_VALORIZACION_BASE, OFICIO_VENTA, REMISION, REMISION_DETALLE, MOVIMIENTO_INVENTARIO y CONFIGURACION_FIRMA.
> 2. Crea la clase `SqliteDbContext` (o ConnectionFactory) utilizando `Microsoft.Data.Sqlite` para SQLite local offline.
> 3. Escribe un script C# ejecutable al iniciar la aplicación (`DatabaseInitializer.cs`) que cree automáticamente la base de datos `granjas_semovientes.db` e inserte los datos semilla iniciales (Granjas: Tesorito, La Cruz, Montelindo; Orientaciones: Lácteo, Carne/Levante, Doble Propósito; Categorías básicas).
> ```

---

#### 📋 PROMPT PASO 2: Servicios de Negocio (AgingEngine y Validaciones)

> **Prompt a copiar y enviar a la IA:**
> ```text
> Actúa como Ingeniero de Software C#. Implementaremos la lógica de negocio central del dominio ganadero.
> 
> Tarea:
> 1. Crea el servicio `AgingEngine.cs` con métodos para:
>    a) Calcular la edad en meses exactos de un semoviente a partir de su fecha de nacimiento.
>    b) Determinar la categoría y precio contable dinámico consultando la tabla de valorización vigente. Implementa la regla de desempate (fallback) por especificidad (priorizar orientación específica sobre genérica 'Todas' y menor edad mínima).
> 2. Crea el servicio `TarifaValidationService.cs` que implemente la fórmula anti-solapamiento: Max(Min_A, Min_B) <= Min(Max_A, Max_B). Si detecta rangos cruzados para el mismo sexo e historia productiva al guardar una tarifa, debe lanzar una `ValidationException` descriptiva.
> 3. Escribe pruebas unitarias con xUnit y FluentAssertions para verificar ambos servicios.
> ```

---

#### 📋 PROMPT PASO 3: Servicio de Ventas, Traslados y Kardex

> **Prompt a copiar y enviar a la IA:**
> ```text
> Actúa como Desarrollador C#. Diseñaremos el servicio de movimientos de inventario `KardexService.cs`.
> 
> Tarea:
> Implementa los siguientes métodos transaccionales en C# (usando transacciones SQLite):
> 1. `RegistrarNacimientoAsync(NacimientoDto dto)`: Inserta en ANIMAL con clase_entrada='NACIMIENTO', valor_compra_origen=0, asigna categoría inicial por edad y registra movimiento de entrada en Kardex.
> 2. `RegistrarCompraAsync(CompraDto dto)`: Registra la compra masiva o individual, guardando el precio de factura en `valor_compra_origen` (inmutable) y `clase_entrada='COMPRA'`.
> 3. `RegistrarVentaAsync(VentaDto dto)`: Valida la jerarquía de 3 niveles (exige Oficio de Venta por programa, Remisión y Número de Recibo de Caja). Actualiza el estado del animal a 'Vendido' y registra el egreso en Kardex guardando `costo_sistema_libros`.
> 4. `RegistrarTrasladoAsync(TrasladoDto dto)`: Genera Remisión de Traslado (sin oficio ni recibo de caja), actualiza `granja_id` del animal y registra el doble movimiento dual en Kardex (-1 Salida en Origen / +1 Entrada en Destino).
> ```

---

#### 📋 PROMPT PASO 4: Motor de Reportes Excel con Firma (`ExcelReportingEngine`)

> **Prompt a copiar y enviar a la IA:**
> ```text
> Actúa como Desarrollador .NET. Implementaremos la exportación de reportes institucionales en Microsoft Excel (.xlsx) mediante ClosedXML.
> 
> Tarea:
> Crea la clase `ExcelReportingEngine.cs` que genere el libro de Excel del "Informe Kardex Financiero de Hatos por Granja" según las especificaciones institucionales:
> 1. Estructura de columnas: Saldo Mes Anterior, Compras, Nacimientos, Ventas (Costo Sistema y Valor Venta Real), Bajas/Muertes, Traslados, Delta Valorización y Saldo Final.
> 2. Formato visual: Estilos profesionales en tonos azul institucional (#1B365D), bordes, formato de moneda ($) y totales.
> 3. Bloque de Firma Institucional: Consulta los datos activos en `CONFIGURACION_FIRMA` (Nombre, Cargo, Facultad) e incrusta al final de la hoja el bloque de texto formateado junto con la imagen de la firma manuscrita digitalizada.
> ```

---

#### 📋 PROMPT PASO 5: Capa de Presentación WPF (MVVM ViewModels y Views)

> **Prompt a copiar y enviar a la IA:**
> ```text
> Actúa como Diseñador y Desarrollador Frontend WPF en C#. Vamos a maquetar la interfaz gráfica de la aplicación.
> 
> Tarea:
> 1. Crea el `BovinosViewModel.cs` y `ConfiguracionViewModel.cs` usando `CommunityToolkit.Mvvm`, declarando propiedades observables, comandos de búsqueda, filtrado por Granja/Programa y acciones de botones.
> 2. Maqueta las vistas XAML correspondientes usando los mockups de referencia del proyecto:
>    a) `BovinosView.xaml`: Tabla maestra de bovinos con barra de filtros superiores y resumen estadístico inferior.
>    b) `ConfiguracionView.xaml`: Interfaz tabulada con 4 pestañas (Tarifas Valorización, Firmas e Identidad, Granjas y Programas, Respaldos/Backup).
> 3. Asegura estilos limpios, paleta de colores coherente y manejo de `DataGrid` estilizados sin Code-Behind en las vistas.
> ```

---

## 6. CHECKLIST DE VERIFICACIÓN PARA LA VERSIÓN BETA (MVP)

Antes de entregar la versión Beta al usuario para pruebas en la granja, verifica:

* [ ] La base de datos SQLite se crea automáticamente al ejecutar la aplicación por primera vez.
* [ ] La pantalla inicial solicita únicamente la contraseña de acceso.
* [ ] Es posible registrar nacimientos con `valor_compra_origen = $0` e historial de genealogía.
* [ ] Es posible registrar compras fijando el valor de factura inmutable.
* [ ] La Ficha del Bovino muestra claramente ambos precios: *Valor Compra Origen* vs *Valor Contable Actual*.
* [ ] El `AgingEngine` actualiza las categorías según la matriz de valorización activa.
* [ ] La pantalla de tarifas bloquea la acción de guardar si existe solapamiento de edades.
* [ ] El módulo de ventas exige Oficio, Remisión y Recibo de Caja.
* [ ] Los traslados mueven el animal de sede y generan los registros duales en Kardex.
* [ ] El informe de Kardex exporta un archivo `.xlsx` legible en Excel con el bloque de firma de la Decana.
* [ ] La función de Backup manual empaqueta correctamente el archivo `.zip`.