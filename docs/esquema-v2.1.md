# Esquema de base de datos v2.1

Base: el esquema "definitivo" v2.0 de `contexto_y_plan_de_desarrollo_ia.md` (§4). La versión implementada
(`PRAGMA user_version = 1`, script `src/UCaldas.Granjas.Data/Schema/schema.sql`) agrega las extensiones de abajo.
Todas se necesitan para cumplir los mockups o las historias de usuario v2.0 y están marcadas `[v2.1]` en el script.

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
- **Versionado:** `PRAGMA user_version`. `DatabaseInitializer` crea el esquema y las semillas en una sola transacción y rechaza versiones desconocidas sin tocar datos.

## Datos semilla (`seed.sql`)

| Tabla | Contenido |
|---|---|
| ORIENTACION_PRODUCTIVA | Lácteo, Carne/Levante |
| PROGRAMA | PRG-GAN Ganadería (Bovinos) INDIVIDUAL; PRG-AVE Avicultura (Aves) LOTE |
| GRANJA | G-01 Tesorito (sugiere Lácteo), G-02 La Cruz (sin sugerencia), G-03 Montelindo (sugiere Carne/Levante) |
| GRANJA_PROGRAMA | PRG-GAN en las tres; PRG-AVE en Montelindo |
| CATEGORIA_ETAPA | Terneros, Levante, Novilla Vientre, Vacas General, Reproductor, Sin clasificar |
| VIGENCIA 2026 (activa) | 9 tarifas de la tabla de precios oficial 2026 (suman $25.710.000) |
| CONFIGURACION_FIRMA | Valores de relleno; el nombre real se configura en la aplicación |
| CONFIGURACION_SISTEMA | Sin fila: su ausencia es el primer arranque (crear contraseña) |
