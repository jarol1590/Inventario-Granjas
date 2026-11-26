# Contexto del Sistema Unificado de Gestión de Granjas

**Universidad de Caldas · Facultad de Ciencias Agropecuarias**
Estado documentado: **v2.12** (`PRAGMA user_version = 12`), actualizado el 26/11/2026.

Este documento da una visión completa y autocontenida del sistema para quien llega sin contexto (una persona del equipo o un asistente de IA). Explica **qué hace, cómo está armado y qué reglas no se pueden romper**. No reemplaza a los documentos de detalle, a los que remite:

| Documento | Para qué sirve |
|---|---|
| [`CLAUDE.md`](CLAUDE.md) | Reglas de código obligatorias, reglas de negocio con su detalle fino y las trampas de WPF ya resueltas. **Es la fuente que manda al escribir código.** |
| [`docs/esquema-v2.1.md`](docs/esquema-v2.1.md) | Esquema real de la base de datos y cada ampliación (v2.1 → v2.9). |
| [`docs/contexto_y_plan_de_desarrollo_ia.md`](docs/contexto_y_plan_de_desarrollo_ia.md) | Especificación funcional original (v2.0). Su stack (.NET 8) quedó superado: hoy es .NET 10. |

## Índice

1. [Qué es el sistema](#1-qué-es-el-sistema)
2. [Stack y estructura del repositorio](#2-stack-y-estructura-del-repositorio)
3. [Arquitectura](#3-arquitectura)
4. [Modelo de datos](#4-modelo-de-datos)
5. [Dominio y reglas de negocio](#5-dominio-y-reglas-de-negocio)
6. [Módulo Remisiones](#6-módulo-remisiones)
7. [Interfaz](#7-interfaz)
8. [Seguridad, acceso y respaldos](#8-seguridad-acceso-y-respaldos)
9. [Informes](#9-informes)
10. [Pruebas y verificación](#10-pruebas-y-verificación)
11. [Comandos útiles](#11-comandos-útiles)
12. [Convenciones de trabajo](#12-convenciones-de-trabajo)
13. [Glosario](#13-glosario)
14. [Dónde seguir leyendo](#14-dónde-seguir-leyendo)

---

## 1. Qué es el sistema

Aplicación de **escritorio, monousuario y 100 % sin conexión** para el control operativo y contable de los semovientes (bovinos) y de la población en lotes (p. ej. aves) de las granjas de la Facultad:

| Granja | Código | Perfil |
|---|---|---|
| Tesorito | G-01 | Sede principal, ganado de leche |
| La Cruz | G-02 | Hato mixto (leche y carne) |
| Montelindo | G-03 | Ganado de carne y avicultura |

Se ejecuta en computadores locales, sin servidor ni internet. Hay una sola contraseña de acceso (no hay perfiles ni roles).

**Qué resuelve**

- **Inventario físico**: cada bovino se controla por su chapeta; las aves y otras especies por lotes de población.
- **Inventario contable** con *doble precio*: lo que costó el animal (inmutable) y su valor contable actual, que el sistema recalcula según la edad y una matriz de precios vigente.
- **Kardex**: un libro mayor de movimientos (entradas, salidas, traslados, valorizaciones) por granja, siempre cuadrado con el inventario.
- **Documentos comerciales**: compras, ventas (Oficio → Remisión → Recibo de Caja SP), traslados entre granjas y bajas (muerte, hurto, descarte sanitario).
- **Venta de subproductos** (leche, huevos, gallinaza, fruta…) en el módulo Remisiones.
- **Informes oficiales** en Excel, con bloque de firmas.

**Programas.** Un *programa* es una línea productiva. Vienen sembrados **Ganadería (Bovinos)** — inventario individual — y **Avicultura (Aves)** — por lotes —. Se pueden crear más desde la interfaz (Configuración › Granjas y Programas), por ejemplo Equinos, o un programa de productos sin animales.

**Módulos superiores de la ventana:** *Sistema Granjas* (el núcleo), *Suministros e Insumos* (pestaña deshabilitada, fuera de alcance) y *Remisiones* (venta de subproductos y recibos de caja).

---

## 2. Stack y estructura del repositorio

| Pieza | Tecnología |
|---|---|
| Plataforma | .NET 10 (LTS), WPF |
| Base de datos | SQLite local, acceso con Dapper |
| MVVM | CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`) |
| Inyección de dependencias | Microsoft.Extensions.DependencyInjection |
| Excel | ClosedXML |
| Gráficas | LiveCharts2 (SkiaSharp) |
| Pruebas | xUnit + FluentAssertions **7.2.0** (no actualizar: la 8.x exige licencia de pago) |

Las versiones están centralizadas en `Directory.Packages.props`.

```
Desarrollo/
├── CLAUDE.md                      Reglas del proyecto
├── ContextoSistemaGranjas.md      Este documento
├── SistemaGranjas.sln
├── Directory.Build.props          Redirige bin/obj fuera de OneDrive
├── Directory.Packages.props       Versiones de paquetes
├── docs/                          Especificación y esquema
├── prototipos/                    Prototipos visuales (no forman parte de la app)
├── src/
│   ├── UCaldas.Granjas.App        WPF: vistas, ViewModels, composición
│   ├── UCaldas.Granjas.Services   Lógica de negocio
│   ├── UCaldas.Granjas.Domain     Entidades, DTOs, constantes, interfaces
│   └── UCaldas.Granjas.Data       SQLite, repositorios, esquema y migraciones
└── tests/
    └── UCaldas.Granjas.Tests      Pruebas xUnit
```

**Dónde viven los datos.** La base está en `%LOCALAPPDATA%\SistemaGranjas\granjas_semovientes.db` (nunca dentro de OneDrive, para no corromperla con la sincronización). Los logs y, por omisión, los respaldos viven en la misma carpeta de datos. La variable de entorno `SISTEMA_GRANJAS_DATOS` redirige esa carpeta: así se prueba la interfaz con datos de demostración sin tocar la base real. La carpeta de respaldos propuesta en Configuración es `C:\UniversidadDeCaldas\Respaldos_Granja` y se puede cambiar.

Si el repositorio está bajo OneDrive, `bin/obj` se redirigen a `%LOCALAPPDATA%\SistemaGranjas\artifacts` (ver `Directory.Build.props`); el ejecutable de Release queda en `artifacts\bin\UCaldas.Granjas.App\release\SistemaGranjas.exe`.

---

## 3. Arquitectura

N capas, con dependencias **solo hacia abajo**:

```
App (WPF / MVVM)  ─►  Services (negocio)  ─►  Domain (entidades, DTOs, interfaces)  ◄─  Data (SQLite / Dapper)
```

`App` compone todo con inyección de dependencias; `Services` solo conoce interfaces definidas en `Domain`; `Data` implementa esas interfaces.

### 3.1 Capa Domain (`src/UCaldas.Granjas.Domain`)

- `Entities/`: `Animal`, `Estructura` (granjas, programas, categorías, razas), `Valorizacion` (vigencias y matriz), `Poblacion` (lotes), `Documentos` (remisiones, oficios, movimientos), `Comercial` (convenios, clientes, productos, recibos).
- `Constants/Constantes.cs`: cadenas de dominio (`EstadoAnimal`, `TipoRemision`, `ClaseSalida`, `FormaPago`, `TipoComprador`, `TipoInventario`, `EstadoLote`…). `Constants/Rutas.cs`: ubicación de respaldos por omisión.
- `Abstractions/`: `IUnitOfWork`, repositorios, `IReloj`, `IDatabaseMaintenance`.
- `Regla.cs`: validaciones de negocio. Lanza `ValidationException` con mensajes claros en español; la interfaz las muestra como advertencia.
- `NumeroALetras.cs`: convierte un valor en pesos a letras (para el recibo de caja); vive en Domain porque la usan Data y Services.

### 3.2 Capa Data (`src/UCaldas.Granjas.Data`)

- Patrón **Repositorio + Unidad de trabajo**: cada operación de negocio abre un `IUnitOfWork` y hace `CommitAsync` al final; si algo falla no queda nada.
- `Repositories/` agrupados por área (animales, documentos, estructura, valorización, comercial) sobre una `RepositoryBase` genérica.
- Convención de Dapper: la clase se mapea a la tabla en `MAYUSCULAS_CON_GUION_BAJO` y cada propiedad a su columna en `snake_case` (p. ej. `Cheque1Num` → `cheque1_num`).
- `Schema/`: `schema.sql` (base nueva), `seed.sql` (semillas: orientaciones, granjas, programas, matriz) y las migraciones (ver 4.3).
- `DatabaseInitializer`: crea la base o la migra en cadena, en una sola transacción. `SqliteDatabaseMaintenance` respalda y restaura.

### 3.3 Capa Services (`src/UCaldas.Granjas.Services`)

| Carpeta | Servicios principales |
|---|---|
| `Kardex/` | **`KardexService`** (clase parcial, un archivo por tema): compras, nacimientos, ventas, bajas, traslados, ediciones, anulaciones, lotes, recibos y productos. `DocumentValidationService` valida la jerarquía documental. |
| `Valorizacion/` | **`AgingEngine`** (edad → categoría → valor contable), `EdadCalculator`, `TarifaSelector`, `TarifaValidationService` (sin solapamiento), `VigenciaService`, `TarifaProgramaService`. |
| `Animales/` | `AnimalService` (alta, ficha editable con recálculo inmediato), `FichaService`. |
| `Lotes/` | `LoteService` (población, etapas, desdoble, bajas, ventas y traslados de lotes). |
| `Catalogos/` | `CatalogoService` (granjas, programas, categorías, razas por programa). |
| `Comercial/` | `OficioService`, `ReciboCajaService` y `ReciboCajaRegla`, `RemisionesQueryService`, `CatalogoComercialService`. |
| `Consultas/` | `ConsultasService` (listados, tableros de Inicio, detalles editables). |
| `Reportes/` | `ExcelReportingEngine` y `KardexReportBuilder`/`KardexLotesBuilder`, `PdfDocumentService`, `FirmaBlockWriter`, `ExcelEstilos`. |
| `Seguridad/` | `AuthService`. |
| `Respaldos/` | `BackupService`. |

**Reloj.** Todo lo que depende de «hoy» usa `IReloj` (nunca `DateTime.Now`), para que las pruebas fijen la fecha.

**Dinero.** Siempre `decimal` en C#. Nunca `SUM` de SQL para totales monetarios. Las fechas se guardan en SQLite como texto ISO.

### 3.4 Capa App (`src/UCaldas.Granjas.App`)

- `Program.cs`: punto de entrada, instancia única.
- `Composicion/`:
  - `RaizDeComposicion`: registra todos los servicios, repositorios y ViewModels.
  - `Arranque`: secuencia base → acceso (contraseña) → cierres de valorización pendientes → ventana principal.
  - `Formularios` y `FormulariosRemisiones`: abren las ventanas modales (detrás de `IFormularios` y `IFormulariosRemisiones`, para poder sustituirlas en pruebas).
- `Infraestructura/`: `ViewModelBase` (con `EjecutarAsync`, que captura excepciones y muestra los errores de negocio), `DialogViewModelBase`, `IDialogService`, `INavegador`, `BarraDePestanas`, `FiltroMensual`, `ContextoGlobal` (Granja y Programa elegidos arriba), comportamientos y convertidores.
- `ViewModels/` y `Vistas/` (XAML). La plantilla que une cada ViewModel con su vista está en `Vistas/Plantillas.xaml`.
- `Estilos/`: `Colores.xaml` (paleta y tipografía) y `Controles.xaml` (estilos). `Recursos/`: logo, ícono y fuentes incrustadas.

### 3.5 Reglas de código (resumen; el texto completo está en `CLAUDE.md`)

1. MVVM estricto: cero lógica de negocio o de datos en el code-behind (`*.xaml.cs` solo `InitializeComponent`).
2. Toda operación de I/O o SQLite es `async/await`.
3. Los ViewModels solo hablan con `IDialogService` e `INavegador`; los errores de negocio se muestran como advertencia con su mensaje.
4. Código limpio, manejo defensivo de excepciones, mensajes en español.
5. **Toda regla nueva lleva prueba xUnit**, y todo ViewModel nuevo lleva prueba con `DialogoFalso`.

---

## 4. Modelo de datos

### 4.1 Tablas (22) por área

| Área | Tablas |
|---|---|
| Configuración y seguridad | `CONFIGURACION_SISTEMA` (hashes de acceso y recuperación), `CONFIGURACION_FIRMA` (firmas de los informes) |
| Estructura | `GRANJA`, `PROGRAMA`, `GRANJA_PROGRAMA`, `ORIENTACION_PRODUCTIVA`, `CATEGORIA_ETAPA`, `RAZA_LINEA_PROGRAMA` |
| Valorización | `VIGENCIA_VALORIZACION`, `TABLA_VALORIZACION_BASE` (matriz bovina por edades), `TARIFA_CATEGORIA_PROGRAMA` (valor de referencia por categoría, programas no bovinos) |
| Inventario | `ANIMAL`, `DATOS_GENEALOGIA`, `LOTE_POBLACION` |
| Documentos y Kardex | `OFICIO_VENTA`, `REMISION`, `REMISION_DETALLE`, `MOVIMIENTO_INVENTARIO` |
| Módulo Remisiones | `CONVENIO`, `CLIENTE`, `CATALOGO_PRODUCTO`, `CONFIGURACION_REMISIONES`, `RECIBO_CAJA` |

Relaciones centrales:

- Un `ANIMAL` pertenece a una `CATEGORIA_ETAPA` (que pertenece a un `PROGRAMA`) y a una `GRANJA`.
- `REMISION` es la tabla de todos los documentos (`tipo_movimiento`: `COMPRA`, `VENTA`, `TRASLADO`, `BAJA`). Sus líneas están en `REMISION_DETALLE`; cada línea es **un animal, un lote o un ítem descrito** (subproducto), con un CHECK que obliga a una sola de las tres.
- Una venta cuelga de un `OFICIO_VENTA` (un solo programa por oficio) y tiene exactamente un `RECIBO_CAJA` (relación 1:1 con `REMISION`).
- `MOVIMIENTO_INVENTARIO` es el **libro mayor** del Kardex.

Texto, fechas y dinero: texto ISO para fechas; en C#, `decimal`. `ANIMAL.raza` y `LOTE_POBLACION.linea_raza` son texto sin llave foránea (inactivar una raza no invalida lo ya registrado).

### 4.2 Detalle fino

El esquema completo, columna por columna y ampliación por ampliación, está en [`docs/esquema-v2.1.md`](docs/esquema-v2.1.md).

### 4.3 Versiones del esquema y migraciones

`DatabaseInitializer` lee `PRAGMA user_version` y aplica en cadena las migraciones que falten, todas en una sola transacción. Una base vacía (versión 0) se crea directamente con `schema.sql` + `seed.sql`. Una versión fuera de rango se rechaza sin tocar datos. Al restaurar un respaldo antiguo, la base vuelve a pasar por el inicializador, así que se migra sola.

| Versión | Qué agregó |
|---|---|
| v2.0 / v2.1 | Esquema base y extensiones aprobadas |
| v2.2 | Recuperación de contraseña y matriz 2026 completa (`migracion_v2.sql`) |
| v2.3 | Programas generalizados (`usa_valorizacion_biologica`, `unidad_venta_default`, categoría «General», tarifas por categoría) y lotes (`migracion_v3.sql`) |
| v2.4 | Documentos de lotes: compra editable, venta y traslado de lotes (`migracion_v4.sql`) |
| v2.5 | Catálogo de razas y líneas por programa (`migracion_v5.sql`) |
| v2.6 | Vacas General con parto desde los 14 meses (`migracion_v6.sql`) |
| v2.7 | `ANIMAL.observaciones`, `MOVIMIENTO_INVENTARIO.observaciones`, lotes sin ubicación ni orden de compra (`migracion_v7.sql`) |
| v2.8 | Módulo Remisiones: convenios, clientes, productos, oficios cerrables, recibos de caja SP (`migracion_v8.sql` + `migracion_v8_detalle.sql`) |
| v2.9 | Líneas de subproducto escritas a mano (`migracion_v9_detalle.sql`) y **conversión de los recibos escritos a mano en Recibos SP** (paso en C# dentro de `DatabaseInitializer`) |
| v2.10 | NIT o cédula del cliente («Otro comprador») opcional: `CLIENTE.nit_cc` admite NULL (`migracion_v10.sql`) |
| v2.11 | Formas de pago del Recibo de Caja: se quita cheque / efectivo y se agregan transferencia electrónica y recursos propios (`migracion_v11.sql`; los recibos anteriores conservan «Efectivo / Cheque») |
| **v2.12** | El recibo guarda entidad, número y tipo de cuenta (Ahorros / Corriente) de la consignación o transferencia, editables y precargados con la cuenta de Datos institucionales (`migracion_v12.sql`) |

Detalles de la v2.9: cada venta sin fila en `RECIBO_CAJA` recibe el siguiente consecutivo (por fecha e id), con forma de pago consignación al banco configurado, el total de sus líneas y la observación «Recibo anterior: …» con el número que tenía escrito; `REMISION.recibo_caja` pasa a «N SP».

---

## 5. Dominio y reglas de negocio

### 5.1 El doble precio

Cada animal tiene dos valores que nunca se confunden:

- **`valor_compra_origen`**: lo que costó. **Inmutable**; $0 si nació en la granja.
- **`valor_contable_actual`**: lo asigna el `AgingEngine` con la matriz de la vigencia activa. Se aplica por igual a animales comprados y nacidos. Un animal con `exento_valorizacion` solo actualiza su categoría.

### 5.2 Valorización bovina (matriz y motor de edades)

- La **matriz** (`TABLA_VALORIZACION_BASE`) define, por vigencia, sexo, si exige partos y orientación, un rango de edad (en meses) y un valor. La matriz 2026 sembrada cubre todas las edades (10 filas); machos de 12–17 meses siguen como Levante y las novillas de más de 24 meses sin parto como Novilla Vientre.
- **Sin solapamiento** por (vigencia, sexo, requiere_partos, orientación) con rangos cerrados: `Max(MinA, MinB) <= Min(MaxA, MaxB)` significa que se solapan. `TarifaValidationService` lo impide al editar.
- **Desempate** al elegir tarifa para un animal (`TarifaSelector`): orientación específica → exige partos → sexo específico → menor edad mínima → menor id.
- **Orientaciones:** solo **Lácteo** y **Carne/Levante** (no existe «Doble Propósito»).
- **Vacas General** pueden tener partos desde los 14 meses; la ficha rechaza partos en animales de menos de 14 meses.
- **Cuándo se valoriza:**
  - *Cierre mensual*: deltas de tipo `Valorizacion` fechados el último día del mes, automáticos al abrir la aplicación (y con un botón para otra fecha). Nunca se asientan fechas anteriores al último cierre.
  - *Al guardar la ficha de un animal*: `AnimalService.ActualizarAsync` recalcula de inmediato categoría y valor (solo animales Disponibles de programas biológicos; exento = solo categoría; sin tarifa = «Sin clasificar» y valor conservado) y, si hay variación, asienta un movimiento `Valorizacion` fechado hoy en la misma transacción.

### 5.3 Programas generalizados

Hay dos caminos de valorización según `PROGRAMA.usa_valorizacion_biologica`:

- **Bovinos** (solo `PRG-GAN`): matriz por edades y `AgingEngine`.
- **Todos los demás** (aves, equinos…): sin cálculo por edad. Un programa nuevo nace con `usa_valorizacion_biologica = 0` (el servicio nunca lo concede), una `unidad_venta_default` (UNIDAD o KG) y la categoría protegida «General». Las categorías/etapas y su valor de referencia se editan en Configuración › Tarifas, una pestaña por programa. En el Kardex, la compra de un animal no bovino vale lo que dice la factura y un nacimiento vale el valor de referencia de la categoría.
- La orientación (Lácteo/Carne) y los partos son solo de bovinos.
- **Razas y líneas por programa:** cada programa tiene su catálogo (activar/inactivar). Todo programa nuevo nace con «No Especificada / Cruce» como opción por defecto (no se puede inactivar). Los selectores son listas cerradas del programa del registro.

### 5.4 Kardex: el libro mayor

- `MOVIMIENTO_INVENTARIO` registra cada hecho con `costo_asociado` **con signo**. La granja afectada es la columna poblada: `granja_destino_id` en entradas y valorizaciones; `granja_origen_id` en salidas. Un traslado genera **dos** filas.
- **Invariante:** la suma del libro por granja es igual a la suma de `valor_contable_actual` de los animales Disponibles de esa granja. Las pruebas lo verifican (`AsegurarInvarianteAsync`).
- El Kardex de hatos solo cuenta movimientos con `animal_id`; los de lotes se leen aparte.

### 5.5 Documentos del Kardex

| Documento | Cómo se maneja |
|---|---|
| **Compra** | Factura con sus animales. Editable y anulable. Una compra de lote lleva solo factura (sin remisión). |
| **Nacimiento** | Alta con valor de compra $0. |
| **Venta** | **Oficio de Venta** (un solo programa) → **Remisión** → **Recibo de Caja SP** (obligatorio). Guardado atómico. |
| **Traslado** | Entre granjas, sin oficio. Editable y anulable; cambiar destino u orientación de un animal exige que no tenga movimientos posteriores. El detalle muestra el costo en libros *del momento del traslado*. |
| **Baja** | Muerte, hurto o descarte sanitario; solo remisión y documento de soporte. |

**Editar = revertir y reaplicar.** Anular restaura el inventario y deja los números consumidos. Compras, ventas y traslados se filtran por programa y granja con los combos globales de arriba.

### 5.6 Lotes (programas por lotes, p. ej. aves)

- Un **lote** (`LOTE_POBLACION`) tiene código, línea/raza, etapa, cantidad inicial, población actual, bajas, valor contable y estado (Activo / Finalizado). Un lote sin conteo (`ControlNumericoPoblacion = 0`) maneja cantidades N/A y su valor total es el del lote.
- **Compra** = factura inmutable; **nacimiento** = compra $0 con el valor de la etapa.
- **Cambio de etapa** recalcula con el valor de referencia (una etapa sin valor conserva el del lote); el **desdoble** es neutro; las **bajas** (muerte, hurto) bajan la población hasta Finalizado.
- **Venta** de lote: Oficio → Remisión → Recibo SP, con `peso_vendido_kg` (obligatorio si el programa vende por KG). **Traslado**: total (el lote cambia de granja) o parcial (se crea un lote destino con `lote_destino_id`).
- **Kardex parcial (v2.7):** solo se asientan la valorización por cambio de etapa y las bajas (con su fecha y motivo). Compras, ventas y traslados de lotes **no** se asientan; el informe los lee de `LOTE_POBLACION` y `REMISION_DETALLE`.
- Un lote ya no pide ubicación ni orden de compra.
- Pendiente: ventas por kilo valorizadas con el Kardex de lotes (valor comercial = `peso_vendido_kg × valor_referencia`) y que el tablero de Inicio lea las bajas fechadas de lotes.

---

## 6. Módulo Remisiones

**Alcance acordado (v2.9).** Remisiones **registra solo ventas de subproductos** de los programas: la leche de un programa bovino, los huevos o la gallinaza del avícola, la fruta de un programa de frutales. Las **ventas y traslados de animales y lotes se hacen únicamente en Sistema Granjas**, pero las ventas **se ven** en la bandeja de Remisiones y se editan con el mismo «Detalle de la salida».

**Pantallas** (menú lateral del módulo): Remisiones (bandeja), Oficios, Recibos Caja, Informes, Configuración (convenios, productos por programa, programas, datos institucionales).

**Flujo de una venta de subproductos**

1. Se elige un **oficio abierto** (fija el programa), fecha, número de remisión (sugerido `REM-n`, editable), granja de origen y comprador (convenio u «otro comprador»).
2. Se escriben los ítems en una **tabla editable** (descripción, unidad, cantidad, valor unitario). La descripción sugiere los productos del catálogo del programa; elegir uno propone su unidad y precio, pero no es obligatorio.
3. «Continuar a Recibo de Caja (SP)» abre la ventana del recibo; remisión, líneas y recibo se guardan **en una sola transacción**. Si algo falla no queda nada ni se consume el consecutivo.
4. Después de guardada, la remisión se consulta, se edita, se anula o se imprime.

**Recibo de Caja SP**

- Consecutivo automático: `MAX(CONFIGURACION_REMISIONES.ultimo_consecutivo_recibo, MAX(numero_recibo)) + 1`. Un recibo anulado **conserva su número**; el número nunca cambia al editarlo.
- Formas de pago de una venta: **consignación bancaria** y **transferencia electrónica** (ambas con entidad bancaria) y **recursos propios**; los recibos anteriores a la v2.11 conservan «Efectivo / Cheque» pero ya no se puede elegir. En los documentos del Kardex, en los documentos del Kardex, **traslado interno**. Admite descuento porcentual; el total se muestra también en letras.
- Los totales se calculan siempre desde las líneas en C# (`TotalesRemision`).
- **Toda venta lleva su Recibo SP**, también las de animales y lotes de Sistema Granjas: al guardarlas se abre esta misma ventana y venta + recibo se guardan juntos. Si un documento llega sin recibo, el Kardex genera uno por defecto (fecha de la venta, consignación al banco configurado).
- La columna de texto `REMISION.recibo_caja` guarda la etiqueta «829 SP» solo en las ventas.

**Oficios**

- Son exclusivos de ventas y de un solo programa. Un oficio **cerrado** no admite remisiones nuevas (animales, lotes ni productos); lo ya emitido sigue editable y anulable, y el oficio se puede **reabrir**.
- Hay un **solo listado de oficios** para ambos módulos: Sistema Granjas crea el oficio al vender si no existe, y un oficio cerrado bloquea las ventas en los dos.
- El programa de un oficio solo cambia si no tiene remisiones.
- **Número automático:** `OF` + 3 primeras letras del programa (sin tildes) + `-` + últimos 2 dígitos del año + consecutivo de 3 cifras, p. ej. `OFGAN-26001`. El consecutivo es **por programa y año**: avanza con cada oficio nuevo y vuelve a 001 al cambiar de año (`OficioService.SiguienteNumeroAsync`, con `IReloj`). Se propone, editable, en «Nuevo oficio» y en la venta de Sistema Granjas cuando no hay un único oficio abierto. Los oficios con formato anterior (`OF-n`) se conservan y no cuentan en el consecutivo.

**Bandeja.** Lista todas las ventas con su clase (Subproductos / Animales / Lotes), recibo SP y estado (Pagada, Pen. Recibo SP, Anulada). Doble clic: los subproductos abren el editor del módulo; los animales y lotes abren el «Detalle de la salida» de Sistema Granjas. Anular una remisión anula su recibo.

**Compradores.** `CONVENIO` (catálogo administrado en Configuración › Convenios) u «Otro comprador» (`CLIENTE`). Nada se borra: se inactiva. Una venta de Sistema Granjas conserva el comprador escrito en texto.

**Datos institucionales** (entidad bancaria, cuenta, jefe de tesorería, consecutivo inicial) en `CONFIGURACION_REMISIONES`.

**Informes.** «Ventas por Programas» (solo `VENTA` vigentes; rango de fechas y programa, por defecto «Todos los programas»): un libro Excel con una hoja por programa (oficio, remisión, recibo de caja, cliente, año, mes, producto, cantidad, valor unitario y total, con subtotal) y una hoja final «Consolidado» que agrupa por programa y oficio, con subtotal por programa y total general, más la firma. La remisión y el recibo de caja también se generan en Excel (botones «Remisión Excel» / «Recibo Excel»).

---

## 7. Interfaz

### 7.1 Estructura de la ventana

La ventana principal (`MainWindow` + `ShellViewModel`) tiene arriba las pestañas de módulo y a la izquierda el menú lateral, que cambia según el módulo:

| Módulo | Secciones del menú |
|---|---|
| Sistema Granjas | Inicio · Animales · Lotes · Compras · Salidas · Traslados · Informes · Configuración |
| Remisiones | Remisiones · Oficios · Recibos Caja · Informes · Configuración |

Los combos **Granja** y **Programa** de la parte superior son filtros globales (`ContextoGlobal`): al cambiarlos la sección vigente se recarga. En Animales el combo de programa se oculta, porque allí cada programa es una pestaña.

### 7.2 Pantallas del módulo Sistema Granjas

- **Inicio**: dos gráficas (por granja y por categoría) que **se dibujan siempre**, en cero si no hay datos (nunca un aviso de «no hay movimientos»), con KPI y acciones rápidas («Registro nacimiento» en programas individuales, «Nuevo lote» en programas de lotes). Con un programa de lotes el tablero cuenta cabezas.
- **Animales (Control Individual)**: un módulo genérico: una pestaña por cada programa con `tipo_inventario = INDIVIDUAL`, todas con la misma vista (`ProgramaTabView`). Ficha editable del animal (con historial, observaciones, genealogía y partos).
- **Lotes** 📦: una pestaña por programa de lotes; ventanas de nuevo lote, cambio de etapa/desdoble, baja y ficha del lote (editable).
- **Compras / Salidas / Traslados**: listados con filtros y un detalle que se abre con doble clic y desde el que se edita o anula. Ventanas modales para registrar (selector de animales, editor de compra, de salida, de traslado).
- **Informes**: Excel de Kardex, inventario físico-financiero, inventario individual por animal y Kardex mensual de lotes, con selección de granja, programa, fechas y firma.
- **Configuración**: Tarifas (matriz bovina y categorías por programa, con vigencias), Granjas y Programas (incluye razas y líneas por programa), Firmas, Respaldos / Backup (incluye configurar la recuperación de contraseña).

### 7.3 Identidad visual (Facultad de Ciencias Agropecuarias)

- **Colores:** azul `#003E70` (principal, el del logo), verde `#66A382` (acento), arena `#D5BB87`, con un degradado verde → azul en la franja de la cabecera. Viven en `Estilos/Colores.xaml`; los informes de Excel usan los mismos.
- **Tipografía:** Roboto (Regular / Medium / Italic) en la interfaz y Roboto Condensed Bold para identificadores institucionales; los informes de Excel usan Times New Roman (son «documentos»). Las fuentes van **incrustadas** (licencia Apache 2.0), no dependen de lo instalado.
- **Logo:** `Recursos/Imagenes/LogoUniversidadDeCaldas.png` y el ícono `SistemaGranjas.ico`.

### 7.4 Trampas de WPF ya resueltas

`CLAUDE.md` lleva la lista completa; las que más se repiten: `DatePicker` y celdas de `DataGrid` necesitan `UpdateSourceTrigger=PropertyChanged`; un `StaticResource` inexistente lanza una excepción al abrir la vista (las pruebas STA cargan todas las vistas del módulo Remisiones para atraparlo); WPF simula la negrita en vez de usar Roboto Medium si se pide `Bold`; Roboto no trae emojis (hay fuentes de respaldo); revertir una selección dentro del mismo enlace exige diferir con `ContextoUi.Diferir`; las gráficas de LiveCharts necesitan una tipografía propia por pincel.

---

## 8. Seguridad, acceso y respaldos

### 8.1 Acceso y recuperación (100 % sin conexión)

- **Primer inicio** (sin fila en `CONFIGURACION_SISTEMA`): ventana de inicialización con contraseña + confirmación + una de las 8 preguntas secretas + su respuesta. Se genera una **clave de recuperación** de 16 caracteres (alfabeto de 31 símbolos sin I, L, O, 0, 1; se muestra como `ABCD-EFGH-JKMN-PQRS`). Solo se ve una vez: se puede copiar al portapapeles o guardar en un `.txt`, y el usuario debe marcar «Ya guardé mi clave» para continuar.
- **Inicios siguientes**: ventana de acceso con la contraseña. «¿Olvidó su contraseña?» valida la **respuesta secreta o la clave** y permite definir una contraseña nueva **sin tocar la base de datos**.
- Todo se guarda como hash **PBKDF2-SHA256** (200.000 iteraciones, sal propia de 16 bytes, comparación en tiempo constante); no se usó BCrypt para no agregar una dependencia. La respuesta se normaliza (sin mayúsculas, tildes ni espacios de más; mínimo 3 caracteres) y la clave se acepta con o sin guiones y en minúsculas.
- **La clave rota** en cada restablecimiento (la usada deja de servir y se entrega otra). Cambiar la contraseña desde Configuración conserva la recuperación.
- Instalaciones antiguas sin datos de recuperación: tras entrar se ofrece configurarla (también en Configuración › Respaldos / Backup, pidiendo la contraseña actual). Sin recuperación configurada, la única salida es reemplazar la base por la de un respaldo.

### 8.2 Respaldos

`BackupService` empaqueta la base en `backup_AAAAMMDD_HHmm.zip`. La copia es consistente aunque la aplicación esté en uso (se hace con `VACUUM`) y el archivo se publica de forma atómica: nunca queda un `.zip` a medias. Se puede consultar el último respaldo y **restaurar** uno; al restaurar, la base pasa de nuevo por el inicializador, así que un respaldo de una versión anterior se migra solo.

---

## 9. Informes

| Informe | Formato | Contenido |
|---|---|---|
| **Kardex financiero** | Excel | Libro mayor por granja y programa, en el rango de fechas elegido. |
| **Inventario físico-financiero** | Excel | Inventario por granja con columna I «Observaciones». |
| **Inventario físico individual** | Excel | Por animal (una hoja por granja, solo Disponibles): clase de entrada, fecha de nacimiento, sexo, chapeta, peso, observaciones. |
| **Kardex mensual de lotes** | Excel | «Movimientos inventario aves»: un bloque de 13 columnas por lote, con subtotal por lote y total general. «Vienen» se reconstruye hacia atrás desde el estado actual del lote. |
| **Hoja de vida del animal** | Excel | Ficha completa del animal. |
| **Ventas por programas** | Excel | Hoja por programa (oficio, remisión, recibo de caja…) y hoja final «Consolidado» con subtotal por programa y total general. |
| **Remisión y recibo de caja** | Excel | Documentos del módulo Remisiones. |

Todos llevan el bloque de firmas de `CONFIGURACION_FIRMA`. Los informes de Excel usan el azul institucional, verde en los totales y Times New Roman. Todos los informes y documentos son **solo Excel**.

---

## 10. Pruebas y verificación

El proyecto `tests/UCaldas.Granjas.Tests` reúne cerca de 880 pruebas (todas verdes al 20/11/2026), organizadas por área:

| Carpeta | Qué prueban |
|---|---|
| `Valorizacion/`, `Animales/`, `Programas/` | Motor de edades, selección y validación de tarifas, vigencias, programas generalizados. |
| `Kardex/` | Compras, ventas, bajas, traslados, ediciones y anulaciones; lotes; cuadre del libro. |
| `Comercial/` | Oficios, recibos, consecutivos y remisiones de subproductos. |
| `Consultas/`, `Reportes/` | Listados, tableros, informes en Excel. |
| `Data/` | Inicializador y migraciones, restricciones de la base, repositorios, unidad de trabajo. |
| `Seguridad/`, `Respaldos/` | Acceso, recuperación de contraseña, respaldo y restauración. |
| `Presentacion/` | ViewModels con dobles de prueba, y carga de vistas en un hilo STA. |

**Infraestructura de pruebas:** `BaseDeDatosDePrueba` (base SQLite temporal), `EscenarioKardex` (escenario con granjas, tarifas y helpers), `RelojFijo` (fecha controlada), `DialogoFalso`, `FormulariosFalso` y `FormulariosRemisionesFalso` (sustituyen las ventanas modales).

**Reglas:** toda regla nueva lleva su prueba; cada ViewModel nuevo, una prueba con `DialogoFalso`. Se verifica además con una corrida real de la aplicación (ver abajo).

**Datos de demostración.** Un par de pruebas opt-in generan datos para revisar a ojo: `DatosDeDemostracion` crea una base con animales de las tres granjas (contraseña de acceso **1234**; recuperación de ejemplo: pregunta 1, respuesta «Firulais», clave `ABCD-EFGH-JKMN-PQRS`), y `MuestrasManuales` produce informes `.xlsx` de muestra.

---

## 11. Comandos útiles

```powershell
# Compilar, probar y ejecutar
dotnet build SistemaGranjas.sln -c Release
dotnet test  SistemaGranjas.sln -c Release
dotnet run --project src/UCaldas.Granjas.App

# Probar la interfaz con datos (no toca la base real)
$env:GRANJAS_DEMO = "C:\demo"; dotnet test --filter DatosDeDemostracion
$env:SISTEMA_GRANJAS_DATOS = "C:\demo"; dotnet run --project src/UCaldas.Granjas.App

# Informes de muestra para revisar a ojo
$env:GRANJAS_MUESTRAS = "C:\muestras"; dotnet test --filter MuestrasManuales
```

`SISTEMA_GRANJAS_DATOS` redirige la carpeta de datos (base, logs y respaldos por omisión).

---

## 12. Convenciones de trabajo

- **Idioma** de la interfaz, los mensajes y los comentarios: español (cultura `es-CO`). Namespaces `UCaldas.Granjas.*`, con namespace de ámbito de archivo.
- **No hacer commits ni push** salvo que el usuario lo pida.
- **Material de referencia** (fuera del repositorio, contiene datos reales; no copiarlo): carpeta hermana `..\referencias` (mockups, logo y colores), `..\informes` (Kardex, inventario y tablas de precios de ejemplo), `..\Diagramas` y los documentos de historias de usuario. El proyecto de Balsamiq `..\mockup sistema.bmpr` es la versión más actual de los mockups (los PNG son anteriores).
- Los archivos fuente suelen llevar BOM y CRLF: conservarlos al editar.

---

## 13. Glosario

| Término | Significado |
|---|---|
| **Chapeta amarilla / roja** | Identificadores físicos del animal (la amarilla es la principal). |
| **Programa** | Línea productiva (Ganadería, Avicultura, Equinos…). |
| **Categoría / etapa** | Clasificación del animal o del lote dentro de un programa. |
| **Matriz de valorización** | Tabla de precios por edad, sexo, orientación y partos. |
| **Vigencia** | Período en el que rige una matriz; solo una está activa. |
| **Exento (de valorización)** | Animal al que solo se le actualiza la categoría, no el valor. |
| **Valor de compra origen** | Costo original del animal; inmutable. |
| **Valor contable actual** | Valor vigente según la matriz y la edad. |
| **Kardex** | Libro mayor de movimientos de inventario. |
| **Oficio de venta** | Documento que agrupa las remisiones de venta de un programa; se puede cerrar. |
| **Remisión** | Documento de movimiento (compra, venta, traslado, baja). |
| **Recibo de Caja SP** | Soporte de recaudo de una venta, con consecutivo propio («829 SP»). |
| **Convenio** | Entidad compradora con la que la Universidad tiene un convenio. |
| **Subproducto** | Producto que sale de un programa sin ser un animal (leche, huevos, gallinaza, fruta). |
| **Lote** | Población de animales controlada en conjunto (p. ej. un galpón). |
| **Desdoble** | División de un lote en otro sin cambio de valor. |
| **Cierre de valorización** | Asiento mensual del cambio de valor contable. |

---

## 14. Dónde seguir leyendo

| Si necesita… | Empiece por… |
|---|---|
| Las reglas de código y las trampas de WPF | [`CLAUDE.md`](CLAUDE.md) |
| El detalle del esquema de la base | [`docs/esquema-v2.1.md`](docs/esquema-v2.1.md) y `src/UCaldas.Granjas.Data/Schema/` |
| La especificación funcional original | [`docs/contexto_y_plan_de_desarrollo_ia.md`](docs/contexto_y_plan_de_desarrollo_ia.md) |
| Cómo se compone la aplicación | `src/UCaldas.Granjas.App/Composicion/` (`Arranque`, `RaizDeComposicion`, `Formularios`) |
| La lógica de ventas, compras, traslados y bajas | `src/UCaldas.Granjas.Services/Kardex/` |
| La valorización bovina | `src/UCaldas.Granjas.Services/Valorizacion/AgingEngine.cs` |
| Lotes | `src/UCaldas.Granjas.Services/Lotes/LoteService.cs` |
| El módulo Remisiones | `src/UCaldas.Granjas.Services/Comercial/` y `src/UCaldas.Granjas.App/ViewModels/Remisiones/` |
| Los informes | `src/UCaldas.Granjas.Services/Reportes/` |
| El acceso y la recuperación de contraseña | `src/UCaldas.Granjas.Services/Seguridad/AuthService.cs` |
| Cómo se prueba | `tests/UCaldas.Granjas.Tests/Kardex/EscenarioKardex.cs` y `Presentacion/` |
