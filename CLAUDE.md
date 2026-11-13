# Sistema Unificado de Gestión de Granjas — Universidad de Caldas

Aplicación de escritorio **monousuario y offline** (WPF, .NET 10 LTS, SQLite) para el control operativo y contable de semovientes (bovinos) de las granjas Tesorito, La Cruz y Montelindo. Fuente de verdad funcional: `docs/contexto_y_plan_de_desarrollo_ia.md` (v2.0). Esquema real implementado: `docs/esquema-v2.1.md` (v2.0 + extensiones aprobadas; `PRAGMA user_version` = 5, o sea v2.5: catálogo de razas y líneas por programa; v2.4: documentos de lotes (compra editable, venta y traslado); v2.3: programas generalizados y lotes; v2.2 = recuperación de contraseña y matriz 2026 completa. Migración automática en cadena desde v1/v2 con `Schema/migracion_v2.sql` y `migracion_v3.sql`).

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

**Programas generalizados (v2.3).** Hay dos caminos de valorización según `PROGRAMA.usa_valorizacion_biologica`: **bovinos** (solo `PRG-GAN`; matriz por edades y `AgingEngine`) y **todos los demás** (Equinos, Avícola...; sin cálculo por edad). Un programa nuevo nace con `usa_valorizacion_biologica = 0` (el servicio nunca lo concede), `unidad_venta_default` (UNIDAD/KG) y la categoría protegida «General» (`CATEGORIA_ETAPA` + `TARIFA_CATEGORIA_PROGRAMA`, `es_default`, valor 0). Las categorías/etapas y su valor de referencia se editan en Configuración › Tarifas (una pestaña por programa; `TarifaProgramaService`). Cada fila de `TARIFA_CATEGORIA_PROGRAMA` va ligada a su `CATEGORIA_ETAPA` (`categoria_id`, ampliación mía a la especificación) porque animales y lotes referencian la categoría. En el Kardex: compra de un animal no bovino = costo de la factura; nacimiento = valor de referencia de la categoría; el `AgingEngine` los salta (`ResultadoValorizacion.NoBiologico`). La orientación (Lácteo/Carne) y los partos son solo de bovinos: en los demás se completa con la primera orientación y la interfaz las oculta.

**Razas y líneas por programa (v2.5).** `RAZA_LINEA_PROGRAMA` (`CatalogoService.GetRazasDeProgramaAsync/AgregarRazaAsync/CambiarEstadoRazaAsync`) reemplaza la lista fija de razas bovinas: cada programa tiene las suyas (bovinos: las razas de siempre; aves: Hy-Line Brown, Ross, Cobb, Lohmann) y todo programa nuevo nace con «No Especificada / Cruce» (`es_default`, no se inactiva; `NombresBase.RazaPorDefecto`). `ANIMAL.raza` y `LOTE_POBLACION.linea_raza` siguen siendo texto sin FK (inactivar una raza no invalida lo registrado); la migración v5 también siembra las razas/líneas ya usadas. Los selectores (Nacimiento, Compra de animales y de lotes, Ficha del animal, Nuevo lote, Ficha del lote) son ComboBox cerrados con `RazasDisponibles` del programa del registro: se recargan al cambiar de programa, una raza de otro programa vuelve a la de por defecto y una raza antigua que ya no esté en el catálogo se sigue mostrando. El CRUD (agregar / activar / inactivar) está en Configuración › Granjas y Programas › «Razas / líneas por programa». El orden es por nombre con cultura es-CO (SQLite no ordena bien las tildes).

**Lotes (📦).** `LotesViewModel` → una pestaña (`ProgramaLoteTabViewModel`) por programa `tipo_inventario = LOTE`; ventanas `NuevoLote`, `CambioEtapaDesdoble`, `BajaLote`, `FichaLote` (editable como la del animal vía `LoteService.ActualizarDatosAsync`: código, ubicación, línea/raza, fecha de ingreso, origen, proveedor/OC, anotaciones, **granja, etapa, cantidad inicial, bajas y valor contable**; se calculan población actual, valor total y Estado; el valor de compra y la entrada son de solo lectura; con la compra anulada no se edita); reglas en `LoteService` (compra = factura inmutable; nacimiento = compra $0 y valor de la etapa; cambio de etapa recalcula con el valor de referencia y una etapa sin valor conserva el del lote; desdoble neutro; bajas hasta Finalizado; lote sin conteo = cantidades N/A y valor total = valor del lote). **Documentos de lotes (v2.4, sin asientos):** la compra de un lote NO lleva remisión, solo factura (vive en el lote) y se abre/edita/anula en la misma pantalla de detalle de compra (`CompraEditorView`, modo lote; `LoteService.EditarCompraAsync/AnularCompraAsync`). La mortandad no lleva documento (solo cambia el lote). La **venta** (Oficio → Remisión → Recibo, con `peso_vendido_kg`, obligatorio si el programa es KG) y el **traslado** (total: el lote cambia de granja; parcial: se crea un lote destino con `lote_destino_id`) sí son remisiones con líneas de lote (`KardexService.Lotes.cs`), editables y anulables con las mismas pantallas de salida y de traslado en modo lote; editar = revertir y reaplicar. `LOTE_POBLACION.operaciones` cuenta etapa/desdoble/baja/venta/traslado: ya no bloquea la compra: cantidad y precio de la compra se corrigen siempre (la población actual se mueve igual que la inicial y no puede quedar negativa; el valor contable acompaña al precio solo si el lote nunca se revaloró) y «Quitar» el lote (= anular la compra) solo se rechaza si ya fue desdoblado, vendido o trasladado, no por bajas ni cambios de etapa; un traslado parcial no se edita ni anula si su lote destino ya tuvo operaciones; `anulado` marca la compra anulada. **El Kardex de lotes NO está implementado** (a la espera de confirmación del usuario): los lotes no escriben en `MOVIMIENTO_INVENTARIO` ni hay ventas/traslados/Excel de lotes; cada operación deja una línea en `observaciones` del lote y el KPI de mortandad es acumulado, no mensual. Las columnas `cantidad` (REMISION_DETALLE, MOVIMIENTO_INVENTARIO) y `peso_vendido_kg` ya existen para ese paso.

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
- **Inicio con un programa de lotes:** el tablero cuenta cabezas (lote sin conteo = 1): las barras por categoría son las etapas del programa (CATEGORIA_ETAPA, ceros incluidos), con KPIs de cabezas y saldo de lotes; los movimientos del mes salen de ingresos de lotes (compra y nacimiento, con lo desdoblado o trasladado sumado de nuevo) y de remisiones de venta y traslado; no hay 'Bajas' porque la mortandad no tiene registro fechado (ConsultasService.DashboardDeLotesAsync).
- **Acciones rápidas (Inicio):** la primera es «Registro nacimiento» con un programa individual y «Nuevo lote» con un programa de lotes (`ShellViewModel.MostrarNuevoLote`; en Animales siempre es individual). «Nuevo lote» abre `NuevoLoteViewModel` (compra o nacimiento a elección) vía `IFormularios.NuevoLoteAsync`.
- **Traslados editables:** igual que las salidas, se consultan en un detalle (lista de Traslados o de Salidas → doble clic) y desde ahí se editan o se anulan. `EditarTrasladoAsync` cambia número, fecha, destino, observaciones, animales y orientación de cada uno; la granja de origen no se cambia. Cambiar el destino o la orientación de un animal, o quitarlo, exige que no tenga movimientos posteriores al traslado (p. ej. un cierre de valorización); los cambios de texto y de fecha siempre se permiten. El detalle muestra la orientación de origen y el costo en libros **del momento del traslado**.
- **Traslados por programa y granja:** `TrasladoFiltro.ProgramaId` con el mismo `FiltroDeProgramaAsync` que Compras/Salidas (programa de la remisión = categoría de sus animales o programa de su lote); la granja filtra por origen **o** destino; en traslados de lotes la cantidad son cabezas y el buscador también encuentra el código del lote.
- **Salidas por programa y granja:** igual que Compras (`SalidaFiltro.ProgramaId`; programa de la remisión = categoría de sus animales o programa de su lote; granja = origen).
- **Compras por programa y granja:** el histórico usa los filtros globales (Granja y Programa; el shell recarga la sección al cambiarlos). Programa individual = una fila por factura (el programa se deduce de la categoría de sus animales; una compra anulada sin animales cae en el programa bovino); programa de lotes = una fila por lote comprado (LOTE_POBLACION, clase_entrada = COMPRA), cuyo detalle abre la ficha del lote. "+ Nueva compra" abre el editor de compras o, en un programa de lotes, "Nuevo lote" en modo compra (Formularios.NuevaCompraAsync lee ContextoGlobal).
- **Ventas por kilo (pendiente con el Kardex de lotes):** valor comercial = `peso_vendido_kg × valor_referencia`; el costo en libros usa las cabezas × valor contable unitario.
- **Informes:** solo Excel (`.xlsx`, ClosedXML) con bloque de firma de `CONFIGURACION_FIRMA`. Azul institucional `#003E70`, verde `#66A382` en los totales y fuente Times New Roman (`ExcelEstilos`).
- Dinero: `decimal` en C#, nunca `SUM` de SQL para totales monetarios. Fechas SQLite como texto ISO.

## Material de referencia (fuera del repo, contiene datos reales)

Carpeta hermana `..\referencias` (mockups PNG, logo y colores de la Facultad), `..\informes` (Kardex/inventario/tabla de precios en Excel y PDF), `..\Diagramas`, y los `.docx` de historias de usuario y notas. No copiar al repo (el logo y las fuentes, que la aplicación sí necesita, están en `src/UCaldas.Granjas.App/Recursos`).

`..\mockup sistema.bmpr` es el proyecto de Balsamiq del usuario y **es la versión más actual de los mockups** (los PNG de `referencias\mockups` son anteriores). Es una base SQLite: `RESOURCES.DATA` guarda cada pantalla como JSON (controles con `typeID`, `x`, `y` y `properties.text`) y `THUMBNAILS.ATTRIBUTES` una miniatura JPEG en Base64. Se lee sobre una copia con `sqlite3`/Python; no se modifica.

## Convenciones

- Idioma de UI, mensajes y comentarios: español (cultura `es-CO`).
- Namespaces `UCaldas.Granjas.*`; archivos con `namespace` de ámbito de archivo.
- No hacer commits ni push salvo que el usuario lo pida.
