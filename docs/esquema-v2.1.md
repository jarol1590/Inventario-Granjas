# Esquema de base de datos v2.1 (con las ampliaciones v2.2 a v2.5)

Base: el esquema "definitivo" v2.0 de `contexto_y_plan_de_desarrollo_ia.md` (§4). La versión implementada
(`PRAGMA user_version = 2`, script `src/UCaldas.Granjas.Data/Schema/schema.sql`) agrega las extensiones de abajo.
Todas se necesitan para cumplir los mockups o las historias de usuario v2.0 y están marcadas `[v2.1]` en el script;
las de la recuperación de contraseña, `[v2.2]`. El nombre del archivo se conserva por las referencias existentes.

## Columnas nuevas

| Tabla | Columna | Motivo |
|---|---|---|
| ANIMAL | `numero_partos INTEGER NOT NULL DEFAULT 0` | La matriz de tarifas usa `requiere_partos` y el esquema v2.0 no guardaba los partos. Se incrementa al registrar un nacimiento cuya madre esté en el sistema. |
| ANIMAL | `exento_valorizacion BOOLEAN NOT NULL DEFAULT 0` | HU-007/015: el animal actualiza su categoría pero conserva su valor contable (toros, genética alta). |
| ANIMAL | `fecha_registro DATETIME NOT NULL` | Momento de alta en el sistema; evita valorizar con cortes anteriores al registro. |
| GRANJA | `orientacion_id INTEGER NULL` | HU-013: sugerir la orientación de la sede destino en los traslados. |
| PROGRAMA | `observaciones TEXT` | El mockup de Granjas y Programas muestra la columna. |
| REMISION | `orden_compra`, `tercero_nit` | Formulario de Compras (HU-009). El N.º de factura / remisión de compra va en `numero_remision`. |
| REMISION | `clase_salida TEXT` | VENTA, MUERTE, HURTO, DESCARTE_SANITARIO o TRASLADO: el tipo BAJA no distingue la causa. |
| REMISION | `anulada BOOLEAN NOT NULL DEFAULT 0` | Anular documentos conservando su número. |
| REMISION_DETALLE | `orientacion_origen_id`, `orientacion_destino_id` | Historial del cambio de orientación al trasladar. |
| CONFIGURACION_SISTEMA | `ultimo_cierre_valorizacion DATE NULL` | Último fin de mes valorizado. |
| CONFIGURACION_SISTEMA | `pregunta_secreta TEXT NULL` `[v2.2]` | Pregunta elegida para recuperar la contraseña (en claro: hay que mostrarla en la pantalla de acceso). |
| CONFIGURACION_SISTEMA | `respuesta_hash TEXT NULL` `[v2.2]` | Hash PBKDF2-SHA256 de la respuesta normalizada (sin mayúsculas, tildes ni espacios de más). |
| CONFIGURACION_SISTEMA | `recovery_key_hash TEXT NULL` `[v2.2]` | Hash PBKDF2-SHA256 de la clave de recuperación (16 caracteres sin guiones). La clave en claro nunca se guarda y se renueva en cada restablecimiento. |

Las tres columnas de recuperación quedan en `NULL` en las bases creadas antes de la v2.2: esa instalación no puede restablecer su contraseña hasta que el usuario configure la recuperación (Configuración › Respaldos / Backup).

## Ampliación v2.3 (`user_version = 3`, `Schema/migracion_v3.sql`)

| Tabla | Cambio |
|---|---|
| PROGRAMA | `usa_valorizacion_biologica` (1 solo en bovinos), `unidad_venta_default` (UNIDAD / KG) |
| TARIFA_CATEGORIA_PROGRAMA (nueva) | categorías y valor de referencia de los programas sin valorización biológica; `categoria_id` liga a `CATEGORIA_ETAPA`; `es_default` = «General» |
| LOTE_POBLACION | recreada: etapa, clase de entrada (COMPRA/NACIMIENTO/TRASLADO/DESDOBLE), línea/raza, origen, proveedor, orden de compra, `control_numerico_poblacion`, valores unitarios y total. Nunca tuvo datos (ninguna pantalla la usaba) |
| REMISION_DETALLE | `peso_vendido_kg`, `cantidad` (1 en animales) |
| MOVIMIENTO_INVENTARIO | `cantidad` (1 en animales) |

La migración da «General» (categoría y tarifa) a cada programa sin valorización biológica.

## Ampliación v2.4 (`user_version = 4`, `Schema/migracion_v4.sql`, solo `ALTER`)

| Tabla | Cambio |
|---|---|
| LOTE_POBLACION | `operaciones` (cambios de etapa, desdobles, bajas, ventas y traslados; solo informativo: ya no bloquea la corrección de la compra; la anulación se rechaza únicamente si el lote fue desdoblado, vendido o trasladado) y `anulado` (compra anulada). La migración marca `operaciones = 1` en los lotes con mortalidad, desdoble o cantidad distinta de la inicial |
| REMISION_DETALLE | `lote_destino_id` (lote creado por un traslado parcial de lotes) |

## Ampliación v2.5 (`user_version = 5`, `Schema/migracion_v5.sql`)

| Tabla | Cambio |
|---|---|
| RAZA_LINEA_PROGRAMA (nueva) | `programa_id`, `nombre` (único por programa, sin distinguir mayúsculas), `es_default` («No Especificada / Cruce»: se crea con el programa y no se inactiva), `activo`. Catálogo de razas (bovinos, equinos...) y líneas genéticas (aves...) de los selectores. `ANIMAL.raza` y `LOTE_POBLACION.linea_raza` siguen siendo texto |

La migración da la raza por defecto a cada programa, siembra las razas bovinas (programa con valorización biológica) y las líneas avícolas (`PRG-AVE`), y agrega sin duplicar las razas distintas ya usadas por animales y lotes.

## Restricciones agregadas

No cambian los datos; hacen que la base misma haga cumplir las reglas del negocio.

- **Jerarquía documental (§3.4):** una `REMISION` de tipo VENTA exige `oficio_id` y un `recibo_caja` no vacío; traslados, bajas y compras los rechazan.
- **Libro del Kardex:** cada `MOVIMIENTO_INVENTARIO` informa **exactamente una** de `granja_origen_id` / `granja_destino_id` (la granja cuyo saldo afecta).
- **Líneas de remisión:** `animal_id` XOR `lote_id`.
- **Una sola vigencia activa:** índice único parcial `ux_vigencia_activa`.
- **Unicidad sin distinguir mayúsculas** (`COLLATE NOCASE`): chapeta amarilla, códigos y nombres de granja, código de programa, N.º de remisión, N.º de oficio, código de lote.
- **CHECK de enumeraciones:** sexo, estado, clase de entrada, tipo de movimiento, tipo de inventario, clase de salida; rango de edad de tarifas (`edad_maxima_meses >= edad_minima_meses`) y valor no negativo.
- **Integridad referencial:** `Foreign Keys=True` en cada conexión; sin `ON DELETE CASCADE` (el código elimina dependencias de forma explícita).
- Booleanos y `valor_compra_origen` pasan a `NOT NULL` con valor por defecto.

## Convenciones de datos

- **Fechas:** texto `yyyy-MM-dd HH:mm:ss` en hora local (ordenable como texto). Los campos de solo fecha usan `00:00:00`.
- **Dinero:** `DECIMAL(12,2)` en SQLite, `decimal` en C#. Los totales se suman en C#, nunca con `SUM()` de SQL (coma flotante).
- **Signo del libro:** `costo_asociado` es positivo en compras, nacimientos y entradas por traslado; negativo en ventas, bajas y salidas por traslado; ± en valorizaciones. Las entradas y valorizaciones usan `granja_destino_id`; las salidas, `granja_origen_id`. Un traslado genera dos filas por animal (−valor en origen, +valor en destino).
- **Inmutabilidad:** `AnimalRepository.UpdateAsync` nunca toca `valor_compra_origen`, `fecha_registro` ni `clase_entrada`; el valor de compra solo cambia con `ActualizarValorCompraOrigenAsync` (corrección de factura al editar una compra).
- **Versionado:** `PRAGMA user_version`. `DatabaseInitializer` (`VersionEsquemaActual = 5`): versión 0 (base vacía) → crea el esquema y las semillas en una sola transacción; versiones 1 a 4 → aplican en cadena las migraciones que falten (`migracion_v2.sql` … `migracion_v5.sql`, todas en una transacción); cualquier otra versión se rechaza sin tocar datos. Al restaurar un respaldo `SqliteDatabaseMaintenance.ReemplazarBaseAsync` vuelve a pasar por el inicializador, así que un respaldo de una versión anterior se migra solo.
- **Hashes de contraseña:** `PBKDF2-SHA256$<iteraciones>$<sal Base64>$<hash Base64>` (200.000 iteraciones, sal propia de 16 bytes, hash de 32 bytes, comparación en tiempo constante). Lo usan `password_hash`, `respuesta_hash` y `recovery_key_hash`.

## Migración v1 → v2 (`Schema/migracion_v2.sql`)

Nunca borra datos y no pisa lo que el usuario haya editado:

1. Agrega las tres columnas de recuperación a `CONFIGURACION_SISTEMA` (vacías).
2. Completa los huecos de la matriz 2026, cada cambio solo si la fila sigue como la sembró el sistema: la fila de Novilla Vientre pasa de 12–24 a 12–999 meses (hembras sin partos, ambas orientaciones) y se agrega "Levante, Macho, 12–17 meses" con el valor de Levante, salvo que ya exista una fila de machos (o de ambos sexos) que cubra esas edades.

## Datos semilla (`seed.sql`)

| Tabla | Contenido |
|---|---|
| ORIENTACION_PRODUCTIVA | Lácteo, Carne/Levante |
| PROGRAMA | PRG-GAN Ganadería (Bovinos) INDIVIDUAL; PRG-AVE Avicultura (Aves) LOTE |
| GRANJA | G-01 Tesorito (sugiere Lácteo), G-02 La Cruz (sin sugerencia), G-03 Montelindo (sugiere Carne/Levante) |
| GRANJA_PROGRAMA | PRG-GAN en las tres; PRG-AVE en Montelindo |
| CATEGORIA_ETAPA | Terneros, Levante, Novilla Vientre, Vacas General, Reproductor, Sin clasificar |
| VIGENCIA 2026 (activa) | 10 tarifas: las 9 de la tabla de precios oficial 2026 (suman $25.710.000) más "Levante, Macho, 12–17 meses" ($1.200.000); la fila de Novilla Vientre (ambas orientaciones) cubre de 12 a 999 meses. Así no quedan edades sin tarifa. |
| CONFIGURACION_FIRMA | Valores de relleno; el nombre real se configura en la aplicación |
| CONFIGURACION_SISTEMA | Sin fila: su ausencia es el primer arranque (crear contraseña) |

## v2.7

- `ANIMAL.observaciones TEXT` (nullable): anotaciones libres editables en la ficha; salen en el inventario físico-financiero (columna I) y en el inventario físico individual.
- `MOVIMIENTO_INVENTARIO.observaciones TEXT` (nullable) e índice `ix_movimiento_lote`: motivo de los asientos de lotes (valorización por cambio de etapa, bajas por mortalidad o hurto).
- `LOTE_POBLACION.ubicacion` y `orden_compra` ya no se capturan: se guardan vacía/nula (la columna sigue NOT NULL por compatibilidad).

## v2.8 (módulo Remisiones)

- `PROGRAMA.maneja_inventario_animal BOOLEAN NOT NULL DEFAULT 1` (0 = programa de productos; se guarda con `tipo_inventario = 'INDIVIDUAL'`).
- `OFICIO_VENTA.cerrado BOOLEAN NOT NULL DEFAULT 0`, `fecha_cierre DATETIME`.
- `REMISION`: `programa_id`, `tipo_comprador`, `convenio_id`, `cliente_id`, `subtotal`, `descuento_porcentaje`, `valor_descuento`, `total` (sin FOREIGN KEY: SQLite no permite quitar después una columna que la lleve).
- `REMISION_DETALLE` reconstruida: `producto_id` (FK a `CATALOGO_PRODUCTO`), `descripcion_item`, `unidad_medida`, `cantidad_producto` y CHECK `(animal_id) + (lote_id) + (producto_id) = 1` (en v2.9 pasa a `… + (producto_id IS NOT NULL OR descripcion_item IS NOT NULL) = 1`).
- Tablas nuevas: `CONVENIO`, `CLIENTE`, `CATALOGO_PRODUCTO`, `CONFIGURACION_REMISIONES` (fila única, consecutivo inicial 828) y `RECIBO_CAJA` (`numero_recibo` y `remision_id` UNIQUE, `forma_pago`, `banco_consignacion`, `es_efectivo`, `cheque1_num`, `cheque2_num`, `total_pagado`, `total_letras`, `observaciones`, `anulado`).


## v2.9 (`user_version = 9`): Remisiones = subproductos; toda venta con Recibo SP

- `REMISION_DETALLE` se reconstruye (`Schema/migracion_v9_detalle.sql`, solo si su CHECK aún es el de v2.8) con el CHECK
  `(animal_id IS NOT NULL) + (lote_id IS NOT NULL) + (producto_id IS NOT NULL OR descripcion_item IS NOT NULL) = 1`: una línea es un
  animal, un lote o un ítem descrito (de catálogo o escrito a mano, sin `producto_id`).
- **Conversión de recibos anteriores** (`DatabaseInitializer.ConvertirRecibosAnterioresAsync`, misma transacción de la migración): cada
  `REMISION` de VENTA sin fila en `RECIBO_CAJA` recibe un Recibo SP con el siguiente consecutivo (orden por fecha e id). Fecha del recibo =
  fecha de la venta; forma de pago = `CONSIGNACION` al banco de `CONFIGURACION_REMISIONES`; total = suma de sus líneas (decimal en C#)
  menos el descuento; `observaciones` = «Recibo anterior: {texto escrito}»; `anulado` = la venta está anulada.
  `REMISION.recibo_caja` pasa a «N SP», se completan `subtotal` y `total` y se actualiza `ultimo_consecutivo_recibo`.
- Reglas de servicio: toda venta (animales, lotes o subproductos) tiene su Recibo SP; si el DTO no lo trae se crea uno por defecto
  (o se conserva el existente al editar). Remisiones solo registra ventas de subproductos; los traslados con recibo siguen
  existiendo en `KardexService` pero se manejan desde Sistema Granjas.

## v2.10 (`user_version = 10`, `Schema/migracion_v10.sql`): NIT / cédula del cliente opcional

- `CLIENTE.nit_cc` pasa a admitir `NULL` (sigue `UNIQUE COLLATE NOCASE`; varios clientes pueden no tener NIT). La tabla se reconstruye conservando datos e ids (ninguna tabla tiene FK hacia `CLIENTE`). El NIT del convenio sigue siendo obligatorio.
