# Sistema Unificado de Gestión de Granjas — Universidad de Caldas

Aplicación de escritorio **monousuario y offline** (WPF, .NET 10 LTS, SQLite) para el control operativo y contable de semovientes (bovinos) de las granjas Tesorito, La Cruz y Montelindo. Fuente de verdad funcional: `docs/contexto_y_plan_de_desarrollo_ia.md` (v2.0). Esquema real implementado: `docs/esquema-v2.1.md` (v2.0 + extensiones aprobadas).

## Comandos

```
dotnet build SistemaGranjas.sln -c Release
dotnet test  SistemaGranjas.sln -c Release
dotnet run --project src/UCaldas.Granjas.App
```

La base de datos vive en `%LOCALAPPDATA%\SistemaGranjas\granjas_semovientes.db` (nunca dentro de OneDrive). Si el repo está bajo OneDrive, `bin/obj` se redirigen a `%LOCALAPPDATA%\SistemaGranjas\artifacts` (ver `Directory.Build.props`).

**Probar la interfaz con datos** (no toca la base real): la variable `SISTEMA_GRANJAS_DATOS` redirige la carpeta de datos (base, logs y respaldos por omisión).

```
$env:GRANJAS_DEMO = "C:\demo"; dotnet test --filter DatosDeDemostracion      # crea una base con animales de las 3 granjas (contraseña 1234)
$env:SISTEMA_GRANJAS_DATOS = "C:\demo"; dotnet run --project src/UCaldas.Granjas.App
```

`GRANJAS_MUESTRAS=<carpeta>` + `--filter MuestrasManuales` genera informes `.xlsx` de muestra para revisarlos a ojo.

## Capas (dependencias solo hacia abajo)

`App` (WPF/MVVM) → `Services` (negocio) → `Domain` (entidades, DTOs, interfaces) ← `Data` (SQLite/Dapper).
`App` compone todo con `Microsoft.Extensions.DependencyInjection`; `Services` solo conoce interfaces del `Domain`.

Dentro de `App`: `Program.cs` (punto de entrada, instancia única) → `Composicion/` (`RaizDeComposicion` = DI, `Arranque` = base → acceso → cierres pendientes → ventana, `Formularios` = abre las ventanas modales) · `Infraestructura/` (`ViewModelBase`, `IDialogService`, `BarraDePestanas`, `FiltroMensual`, comportamientos y convertidores) · `ViewModels/` · `Vistas/` (XAML; las plantillas VM→vista están en `Vistas/Plantillas.xaml`) · `Estilos/` (paleta `#1B365D` y estilos de controles).

## Reglas de código (obligatorias)

1. MVVM estricto con `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`).
2. **Cero lógica de negocio o de datos en el code-behind** (`*.xaml.cs` solo `InitializeComponent`).
3. Toda operación de I/O y SQLite es `async/await`.
4. Patrón Repositorio + Servicio; transacciones multi-repositorio con `IUnitOfWork`.
5. Código limpio, manejo defensivo de excepciones, validaciones con mensajes claros en español.
6. Cumplir al 100% las reglas de negocio (abajo). Toda regla nueva lleva prueba xUnit.
7. `FluentAssertions` fijado en **7.2.0** (la 8.x exige licencia de pago). No actualizar.
8. Los ViewModels solo hablan con `IDialogService` (mensajes, archivos, ventanas modales) y `INavegador`; los errores de negocio (`ValidationException`) se muestran como advertencia con su mensaje, vía `ViewModelBase.EjecutarAsync`. Cada ViewModel nuevo lleva prueba con `DialogoFalso` (`tests/.../Presentacion`).

### Trampas de WPF ya resueltas (no repetirlas)

- WPF quita `System.IO` de los usings implícitos: está como `global using` en `App/GlobalUsings.cs`.
- Al cerrarse una ventana, `Application.MainWindow` pasa al primer `Window` nuevo: no cerrar "a mano" la ventana de acceso (ya se cerró con `ShowDialog`).
- `DatePicker.SelectedDate` y los campos dentro de `DataGrid` necesitan `UpdateSourceTrigger=PropertyChanged` (el texto tecleado se confirma después del `LostFocus`).
- Revertir una selección dentro del mismo enlace no refresca el control: usar `ContextoUi.Diferir`.
- `PasswordBox`: usar `ContrasenaAsistente` (valor predeterminado `null`, si no el primer enlace nunca engancha el evento).
- Las ventanas modales se abren con `IDialogService.Mostrar`; `DialogService` lleva la pila de modales para que los mensajes y ventanas hijas tengan como dueña la que está encima.

## Reglas de negocio que no se pueden romper

- **Doble precio:** `valor_compra_origen` es inmutable ($0 si nació en la granja); `valor_contable_actual` lo asigna el `AgingEngine` con la matriz de la vigencia activa (compradas y nacidas por igual; `exento_valorizacion` solo actualiza categoría).
- **Orientaciones:** solo Lácteo y Carne/Levante (no existe Doble Propósito).
- **Ventas:** Oficio de Venta (un solo programa) → Remisión → Recibo de Caja obligatorio. Traslados, bajas y compras solo Remisión (rechazan oficio/recibo).
- **Kardex = libro mayor** de `MOVIMIENTO_INVENTARIO`: `costo_asociado` con signo; la granja afectada es la columna poblada (`granja_destino_id` en entradas y valorización, `granja_origen_id` en salidas); un traslado genera 2 filas. Invariante: Σ libro por granja = Σ `valor_contable_actual` de los Disponibles.
- **Valorización mensual:** deltas `Valorizacion` fechados el último día del mes; nunca fechas anteriores al último cierre.
- **Tarifas:** sin solapamiento por (vigencia, sexo, requiere_partos, orientación) con rangos cerrados: `Max(MinA,MinB) <= Min(MaxA,MaxB)`. Desempate: orientación específica → exige partos → sexo específico → menor edad mínima → menor id.
- **Informes:** solo Excel (`.xlsx`, ClosedXML) con bloque de firma de `CONFIGURACION_FIRMA`. Azul institucional `#1B365D`.
- Dinero: `decimal` en C#, nunca `SUM` de SQL para totales monetarios. Fechas SQLite como texto ISO.

## Material de referencia (fuera del repo, contiene datos reales)

Carpeta hermana `..\referencias` (mockups PNG, informes de muestra), `..\informes` (Kardex/inventario/tabla de precios en Excel y PDF), `..\Diagramas`, y los `.docx` de historias de usuario y notas. No copiar al repo.

## Convenciones

- Idioma de UI, mensajes y comentarios: español (cultura `es-CO`).
- Namespaces `UCaldas.Granjas.*`; archivos con `namespace` de ámbito de archivo.
- No hacer commits ni push salvo que el usuario lo pida.
