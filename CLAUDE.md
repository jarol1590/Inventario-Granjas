# Sistema Unificado de Gestión de Granjas — Universidad de Caldas

Aplicación de escritorio **monousuario y offline** (WPF, .NET 10 LTS, SQLite) para el control operativo y contable de semovientes (bovinos) de las granjas Tesorito, La Cruz y Montelindo. Fuente de verdad funcional: `docs/contexto_y_plan_de_desarrollo_ia.md` (v2.0). Esquema real implementado: `docs/esquema-v2.1.md` (v2.0 + extensiones aprobadas; `PRAGMA user_version` = 2, o sea v2.2: columnas de recuperación de contraseña y matriz 2026 completa, con migración automática desde v1 en `Schema/migracion_v2.sql`).

## Comandos

```
dotnet build SistemaGranjas.sln -c Release
dotnet test  SistemaGranjas.sln -c Release
dotnet run --project src/UCaldas.Granjas.App
```

La base de datos vive en `%LOCALAPPDATA%\SistemaGranjas\granjas_semovientes.db` (nunca dentro de OneDrive). Si el repo está bajo OneDrive, `bin/obj` se redirigen a `%LOCALAPPDATA%\SistemaGranjas\artifacts` (ver `Directory.Build.props`).

**Probar la interfaz con datos** (no toca la base real): la variable `SISTEMA_GRANJAS_DATOS` redirige la carpeta de datos (base, logs y respaldos por omisión).

```
$env:GRANJAS_DEMO = "C:\demo"; dotnet test --filter DatosDeDemostracion      # crea una base con animales de las 3 granjas (contraseña 1234; recuperación: pregunta 1, respuesta «Firulais», clave ABCD-EFGH-JKMN-PQRS)
$env:SISTEMA_GRANJAS_DATOS = "C:\demo"; dotnet run --project src/UCaldas.Granjas.App
```

`GRANJAS_MUESTRAS=<carpeta>` + `--filter MuestrasManuales` genera informes `.xlsx` de muestra para revisarlos a ojo.

## Capas (dependencias solo hacia abajo)

`App` (WPF/MVVM) → `Services` (negocio) → `Domain` (entidades, DTOs, interfaces) ← `Data` (SQLite/Dapper).
`App` compone todo con `Microsoft.Extensions.DependencyInjection`; `Services` solo conoce interfaces del `Domain`.

Dentro de `App`: `Program.cs` (punto de entrada, instancia única) → `Composicion/` (`RaizDeComposicion` = DI, `Arranque` = base → acceso → cierres pendientes → ventana, `Formularios` = abre las ventanas modales) · `Infraestructura/` (`ViewModelBase`, `IDialogService`, `BarraDePestanas`, `FiltroMensual`, `EspecieDePrograma`, comportamientos y convertidores) · `ViewModels/` · `Vistas/` (XAML; las plantillas VM→vista están en `Vistas/Plantillas.xaml`) · `Estilos/` (`Colores.xaml` = paleta y tipografía, `Controles.xaml` = estilos) · `Recursos/` (logo, ícono y fuentes incrustados).

**Animales (Control Individual)** es un módulo genérico: `AnimalesViewModel.Programas` (una `ProgramaTabViewModel` por cada fila de `PROGRAMA` con `tipo_inventario = INDIVIDUAL`) alimenta el `TabControl` de `AnimalesView.xaml`; todas las pestañas usan la misma vista (`ProgramaTabView`) y filtran grilla, categorías (`GetCategoriasDeProgramaAsync`) y totales por su programa. Un programa individual nuevo aparece solo como pestaña; el nombre, plural, singular e ícono salen del nombre del programa (`EspecieDePrograma`). Las categorías y tarifas de otras especies todavía no se crean desde la interfaz.

**Acceso y recuperación de contraseña (100 % sin conexión).** `Arranque`: sin fila en `CONFIGURACION_SISTEMA` abre `InicializacionPasswordWindow` (contraseña + confirmación + una de las 8 preguntas de `AuthService.PreguntasSecretas` + respuesta) y `AuthService.CrearAccesoAsync` genera la **clave de recuperación** de 16 caracteres (alfabeto de 31 símbolos sin I, L, O, 0, 1; se muestra `ABCD-EFGH-JKMN-PQRS`; solo se ve una vez). `ClaveDeRecuperacionViewModel` la copia al portapapeles o la guarda en un `.txt` (UTF-8 con BOM) y el usuario debe marcar "Ya guardé mi clave" para continuar. Si hay fila, abre `LoginWindow`; "¿Olvidó su contraseña?" (`LoginViewModel`, pasos `Ingreso` → `Recuperacion` → `ClaveNueva`) valida la respuesta secreta **o** la clave y redefine la contraseña **sin tocar la base de datos**.
- Todo se guarda como hash **PBKDF2-SHA256** (200.000 iteraciones, sal propia de 16 bytes, comparación en tiempo constante; no se usó BCrypt para no agregar una dependencia) en `CONFIGURACION_SISTEMA`: `password_hash`, `respuesta_hash`, `recovery_key_hash` (la `pregunta_secreta` va en claro: hay que mostrarla). La respuesta se normaliza (sin mayúsculas, tildes ni espacios de más, mínimo 3 caracteres); la clave se acepta con o sin guiones y en minúsculas.
- **La clave rota en cada restablecimiento**: la usada (o la que alguien pudo ver) deja de servir y se entrega una nueva. Cambiar la contraseña desde Configuración conserva la recuperación.
- Instalaciones anteriores (sin datos de recuperación): no se puede restablecer hasta configurarla. `Arranque.OfrecerConfigurarRecuperacionAsync` lo ofrece tras entrar y Configuración › Respaldos / Backup tiene la sección (`ConfigurarRecuperacionViewModel`, pide la contraseña actual). Sin recuperación configurada la única salida es reemplazar la base por la de un respaldo.

## Identidad visual (Universidad de Caldas · Facultad de Ciencias Agropecuarias)

- **Colores** (medidos de `referencias\Colores Facultad Agropecuaria.png`): azul `#003E70` (principal; es el azul del logo), verde `#66A382` (acento), arena `#D5BB87`; degradado verde → azul en la franja de la cabecera. Todo vive en `Estilos/Colores.xaml`; los informes de Excel usan los mismos (`ExcelEstilos`).
- **Tipografía** (guía oficial): Roboto Regular / Medium / Italic en toda la interfaz; Roboto Condensed Bold para identificadores institucionales (estilo `Texto.Identificador`); Times New Roman para los informes de Excel (son "documentos"). Las fuentes van incrustadas en `Recursos/Fuentes` (licencia Apache 2.0 incluida), no dependen de lo instalado.
- **Logo**: `Recursos/Imagenes/LogoUniversidadDeCaldas.png` (recorte del original con fondo transparente) y `SistemaGranjas.ico` (emblema). El original está en `..\referencias`.

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
- **Fuentes incrustadas** (`FontFamily="/Recursos/Fuentes/#Roboto, Segoe UI Emoji, Segoe UI Symbol"`): (1) el énfasis se pide con `FontWeight="Medium"`; con `SemiBold`/`Bold` WPF simula la negrita sobre Regular en vez de usar Roboto Medium. (2) WPF mete Roboto Condensed dentro de la familia "Roboto" con ancho `Condensed`: se pide con `FontStretch="Condensed"` + `Bold`, no con `#Roboto Condensed`. (3) Roboto no trae emojis: sin la lista de respaldo, íconos como 🪪 salen como cuadro. (4) Un `<Style TargetType="Window">` no llega a las ventanas derivadas: la fuente se pone en cada `Window` (`LoginWindow`, `MainWindow`, `DialogHostWindow`) y las ayudas emergentes la reciben del estilo de `ToolTip`.
- El `ControlTemplate` de un `TabControl` propio debe llevar `x:Name="PART_SelectedContentHost"` en su `ContentPresenter`; si no, Windows UI Automation no ve el contenido de la pestaña.
- Columnas de `DataGrid` con `*`: si la suma de los `MinWidth` supera el ancho, las demás se aplastan y el texto centrado se recorta por ambos lados. Dar a cada columna un `MinWidth` suficiente para su encabezado y su dato más largo.
- **Foco en asistentes de varios pasos** (acceso, primer inicio, configurar recuperación): al ocultarse el control que tenía el foco este cae en un contenedor y, con el teclado, WPF dibuja un rectángulo punteado alrededor de toda la tarjeta. Los `ScrollViewer` ya no son enfocables (estilo global en `Controles.xaml`) y cada panel que aparece lleva el foco a su primer control con `FocoAlMostrarse` (`Activo` + `Destino`). No usarlo en los `TextBox` que se alternan con botones de opción: el foco saltaría mientras el usuario recorre las opciones con las flechas.
- Varios botones `IsDefault` en una misma ventana (uno por panel; los de los paneles colapsados no cuentan): WPF solo atiende el visible. Ojo: si un `Button` tiene el foco, Enter lo activa a él y no al botón predeterminado.
- **LiveCharts**: (1) cada `SolidColorPaint` necesita su propia `SKTypeface` (`Graficas.NuevaTipografia`): LiveCharts la libera al descartar la gráfica y una compartida dejaba en blanco las gráficas siguientes al salir de Inicio y volver. (2) Una barra de tamaño cero no dibuja su etiqueta: el cero se escribe en el nombre de la categoría (`Graficas.Etiquetar`; en dos líneas en las columnas, para que no se pisen). (3) El eje numérico se fuerza a 0–4 como mínimo para que una gráfica vacía no colapse.

## Reglas de negocio que no se pueden romper

- **Doble precio:** `valor_compra_origen` es inmutable ($0 si nació en la granja); `valor_contable_actual` lo asigna el `AgingEngine` con la matriz de la vigencia activa (compradas y nacidas por igual; `exento_valorizacion` solo actualiza categoría).
- **Orientaciones:** solo Lácteo y Carne/Levante (no existe Doble Propósito).
- **Ventas:** Oficio de Venta (un solo programa) → Remisión → Recibo de Caja obligatorio. Traslados, bajas y compras solo Remisión (rechazan oficio/recibo).
- **Kardex = libro mayor** de `MOVIMIENTO_INVENTARIO`: `costo_asociado` con signo; la granja afectada es la columna poblada (`granja_destino_id` en entradas y valorización, `granja_origen_id` en salidas); un traslado genera 2 filas. Invariante: Σ libro por granja = Σ `valor_contable_actual` de los Disponibles.
- **Valorización mensual:** deltas `Valorizacion` fechados el último día del mes; nunca fechas anteriores al último cierre.
- **Tarifas:** sin solapamiento por (vigencia, sexo, requiere_partos, orientación) con rangos cerrados: `Max(MinA,MinB) <= Min(MaxA,MaxB)`. Desempate: orientación específica → exige partos → sexo específico → menor edad mínima → menor id. La matriz 2026 sembrada cubre todas las edades (10 filas): machos de 12–17 meses siguen como Levante (1.200.000) y novillas de más de 24 meses sin parto siguen como Novilla Vientre (rango 12–999, ambas orientaciones); la migración v1→v2 rellena esos huecos en bases existentes sin pisar lo que el usuario haya editado.
- **Inicio:** las dos gráficas se dibujan siempre, en cero si no hay datos (todas las granjas y categorías del programa, aunque valgan 0); nunca un aviso de "no hay movimientos".
- **Traslados editables:** igual que las salidas, se consultan en un detalle (lista de Traslados o de Salidas → doble clic) y desde ahí se editan o se anulan. `EditarTrasladoAsync` cambia número, fecha, destino, observaciones, animales y orientación de cada uno; la granja de origen no se cambia. Cambiar el destino o la orientación de un animal, o quitarlo, exige que no tenga movimientos posteriores al traslado (p. ej. un cierre de valorización); los cambios de texto y de fecha siempre se permiten. El detalle muestra la orientación de origen y el costo en libros **del momento del traslado**.
- **Informes:** solo Excel (`.xlsx`, ClosedXML) con bloque de firma de `CONFIGURACION_FIRMA`. Azul institucional `#003E70`, verde `#66A382` en los totales y fuente Times New Roman (`ExcelEstilos`).
- Dinero: `decimal` en C#, nunca `SUM` de SQL para totales monetarios. Fechas SQLite como texto ISO.

## Material de referencia (fuera del repo, contiene datos reales)

Carpeta hermana `..\referencias` (mockups PNG, logo y colores de la Facultad), `..\informes` (Kardex/inventario/tabla de precios en Excel y PDF), `..\Diagramas`, y los `.docx` de historias de usuario y notas. No copiar al repo (el logo y las fuentes, que la aplicación sí necesita, están en `src/UCaldas.Granjas.App/Recursos`).

`..\mockup sistema.bmpr` es el proyecto de Balsamiq del usuario y **es la versión más actual de los mockups** (los PNG de `referencias\mockups` son anteriores). Es una base SQLite: `RESOURCES.DATA` guarda cada pantalla como JSON (controles con `typeID`, `x`, `y` y `properties.text`) y `THUMBNAILS.ATTRIBUTES` una miniatura JPEG en Base64. Se lee sobre una copia con `sqlite3`/Python; no se modifica.

## Convenciones

- Idioma de UI, mensajes y comentarios: español (cultura `es-CO`).
- Namespaces `UCaldas.Granjas.*`; archivos con `namespace` de ámbito de archivo.
- No hacer commits ni push salvo que el usuario lo pida.
