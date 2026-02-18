using System.ComponentModel;
using System.Globalization;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Servicio de localización basado en diccionarios.
/// Lee la preferencia "app_language" (es/en) y expone traducciones vía indexer.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    public event PropertyChangedEventHandler? PropertyChanged;

    private string _currentLanguage;
    private Dictionary<string, string> _currentStrings;

    private LocalizationService()
    {
        _currentLanguage = Preferences.Default.Get("app_language", "es");
        _currentStrings = _currentLanguage == "en" ? _en : _es;
    }

    /// <summary>Idioma actual ("es" o "en").</summary>
    public string CurrentLanguage => _currentLanguage;

    /// <summary>Indexer para obtener la traducción de una clave.</summary>
    public string this[string key] => GetString(key);

    /// <summary>Obtiene la traducción para la clave dada.</summary>
    public string GetString(string key)
    {
        if (_currentStrings.TryGetValue(key, out var val))
            return val;
        // Fallback a español
        if (_es.TryGetValue(key, out var fallback))
            return fallback;
        return $"[{key}]";
    }

    /// <summary>Obtiene un StringFormat traducido y lo aplica al valor.</summary>
    public string Format(string key, object arg0)
    {
        var format = GetString(key);
        return string.Format(format, arg0);
    }

    /// <summary>Cambia el idioma en tiempo de ejecución.</summary>
    public void SetLanguage(string lang)
    {
        if (lang == _currentLanguage) return;
        _currentLanguage = lang;
        _currentStrings = lang == "en" ? _en : _es;
        Preferences.Default.Set("app_language", lang);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); // refrescar todo
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    // ═══════════════════════════════════════════════════════════════
    //  ESPAÑOL
    // ═══════════════════════════════════════════════════════════════
    private static readonly Dictionary<string, string> _es = new()
    {
        // ── Sidebar ──────────────────────────────────────────────
        ["Sidebar_OrgLibrary"] = "Biblioteca de organización",
        ["Sidebar_PersonalLibrary"] = "Biblioteca personal",
        ["Sidebar_GeneralGallery"] = "Galería General",
        ["Sidebar_Sessions"] = "Sesiones",
        ["Sidebar_VideoLessons"] = "Videolecciones",
        ["Sidebar_Favorites"] = "Favoritos",
        ["Sidebar_Videos"] = "Videos",
        ["Sidebar_Trash"] = "Papelera",
        ["Sidebar_SmartFolder"] = "Carpeta inteligente",
        ["Sidebar_Diary"] = "Diario",

        // ── Main content ─────────────────────────────────────────
        ["Main_SelectAll"] = "Seleccionar todo",
        ["Main_SelectSession"] = "Selecciona una sesión",
        ["Main_SessionVideosHere"] = "Los videos de la sesión aparecerán aquí",

        // ── Right panel titles ───────────────────────────────────
        ["RightPanel_ActionsAnalysis"] = "Acciones y Análisis",
        ["RightPanel_FiltersStats"] = "Filtros y Estadísticas",
        ["RightPanel_SessionDiary"] = "Diario de Sesión",

        // ── Batch actions (local) ────────────────────────────────
        ["Batch_BatchActions"] = "Acciones en lote",
        ["Batch_VideoSelection"] = "Selección de vídeos",
        ["Batch_Select"] = "Seleccionar",
        ["Batch_All"] = "Todos",
        ["Batch_Playlist"] = "Playlist",
        ["Batch_Edit"] = "Editar",
        ["Batch_Share"] = "Compartir",
        ["Batch_Delete"] = "Eliminar",

        // ── Batch actions (remote) ───────────────────────────────
        ["Batch_RemoteVideoSelection"] = "Selección de vídeos remotos",
        ["Batch_DownloadToPersonal"] = "Descargar a personal",
        ["Batch_AddToPersonal"] = "Añadir a personal",
        ["Batch_RemovePersonal"] = "Quitar personal",
        ["Batch_DeleteCloud"] = "Eliminar nube",

        // ── Analysis ─────────────────────────────────────────────
        ["Analysis_TechAnalysis"] = "Análisis técnico",
        ["Analysis_QuickAnalysis"] = "Análisis rápido",
        ["Analysis_IsolatedMode"] = "Modo aislado",        ["Analysis_Clean"] = "Limpiar",
        // ── Filters ──────────────────────────────────────────────
        ["Filter_Filters"] = "Filtros",        ["Filter_Clean"] = "Limpiar",        ["Filter_Place"] = "Lugar",
        ["Filter_Athlete"] = "Deportista",
        ["Filter_Section"] = "Sección",
        ["Filter_Tag"] = "Tag",
        ["Filter_DateFrom"] = "Fecha desde",
        ["Filter_DateTo"] = "Fecha hasta",

        // ── Stats ────────────────────────────────────────────────
        ["Stats_DataStats"] = "Datos y Estadísticas",
        ["Stats_Videos"] = "Vídeos",
        ["Stats_Time"] = "Tiempo",
        ["Stats_Sessions"] = "Sesiones",

        // ── Split times ──────────────────────────────────────────
        ["SplitTimes_BySection"] = "⏱ Tiempos por Sección",
        ["SplitTimes_Open"] = "Abrir",
        ["SplitTimes_Times"] = "Tiempos",
        ["SplitTimes_VsYou"] = "vs Tú",

        // ── Table headers ────────────────────────────────────────
        ["Table_Athlete"] = "ATLETA",
        ["Table_Category"] = "CAT.",
        ["Table_Time"] = "TIEMPO",
        ["Table_Penalty"] = "PENAL.",
        ["Table_Total"] = "TOTAL",
        ["Table_VsYou"] = "vs TÚ",

        // ── Tag stats ────────────────────────────────────────────
        ["TagStats_TaggedNamed"] = "Vídeos etiquetados/nombrados",
        ["TagStats_EventTags"] = "Tags de eventos",
        ["TagStats_TotalEvents"] = "eventos totales",
        ["TagStats_UniqueTypes"] = "tipos únicos",
        ["TagStats_MostUsedEvents"] = "Eventos más utilizados",
        ["TagStats_Labels"] = "Etiquetas",
        ["TagStats_Assigned"] = "asignadas",
        ["TagStats_Unique"] = "únicas",
        ["TagStats_PerSession"] = "por sesión",
        ["TagStats_MostUsedLabels"] = "Etiquetas más utilizadas",

        // ── Recording session popup ──────────────────────────────
        ["RecPopup_Title"] = "Nueva sesión de grabación",
        ["RecPopup_Subtitle"] = "Crea una sesión y graba vídeos directamente",
        ["RecPopup_Place"] = "Lugar *",
        ["RecPopup_PlacePH"] = "Ej: Canal olímpico",
        ["RecPopup_SessionName"] = "Nombre de sesión *",
        ["RecPopup_SessionNamePH"] = "Ej: Entrenamiento mañana",
        ["RecPopup_SessionType"] = "Tipo de sesión * (cuartos, medios, largos, etc.)",
        ["RecPopup_CreateRecord"] = "Crear y grabar",

        // ── Smart folder popup ───────────────────────────────────        ["SmartFolder_Name"] = "Nombre",
        ["SmartFolder_NamePlaceholder"] = "Nombre de la carpeta",        ["SmartFolder_Subtitle"] = "Define criterios para agrupar vídeos automáticamente",
        ["SmartFolder_Conditions"] = "Condiciones",
        ["SmartFolder_And"] = "Y (AND)",
        ["SmartFolder_Or"] = "O (OR)",
        ["SmartFolder_ValuePH"] = "Valor",
        ["SmartFolder_SecondDatePH"] = "Segundo valor (fecha)",
        ["SmartFolder_AddCondition"] = "Añadir condición",

        // ── Color / Icon popup ───────────────────────────────────
        ["Popup_Color"] = "Color",
        ["Popup_Icon"] = "Icono",

        // ── Batch edit popup ─────────────────────────────────────
        ["BatchEdit_Title"] = "Editar vídeos seleccionados",
        ["BatchEdit_AssignAthlete"] = "Asignar atleta",
        ["BatchEdit_LastNamePH"] = "Apellido",
        ["BatchEdit_FirstNamePH"] = "Nombre",
        ["BatchEdit_NoAthletes"] = "No hay atletas disponibles",
        ["BatchEdit_AssignSection"] = "Asignar sección/tramo",
        ["BatchEdit_Range"] = "(1-99)",
        ["BatchEdit_AddTags"] = "Añadir etiquetas",
        ["BatchEdit_NewTagPH"] = "Nueva etiqueta",
        ["BatchEdit_NoTags"] = "No hay etiquetas disponibles",

        // ── Edit session popup ───────────────────────────────────
        ["EditSession_Title"] = "Editar sesión",
        ["EditSession_Name"] = "Nombre",
        ["EditSession_NamePH"] = "Nombre de la sesión",
        ["EditSession_Place"] = "Lugar",
        ["EditSession_PlacePH"] = "Lugar de la sesión",
        ["EditSession_SessionType"] = "Tipo de sesión",
        ["EditSession_SessionTypePH"] = "Tipo de sesión",

        // ── Split times popup (detail) ───────────────────────────
        ["SplitPopup_Title"] = "Tiempos por Sección",
        ["SplitPopup_Sections"] = "Secciones",
        ["SplitPopup_HTML"] = "HTML",
        ["SplitPopup_PDF"] = "PDF",
        ["SplitPopup_Legend"] = "Lap = tiempo parcial · Acum. = tiempo acumulado · CV% = Coeficiente de Variación = (σ / x̄) × 100 (menor = más consistente)",

        // ── Report sections popup ────────────────────────────────
        ["Report_Title"] = "Secciones del informe",
        ["Report_QuickSummary"] = "Resumen rápido",
        ["Report_FullAnalysis"] = "Análisis completo",
        ["Report_AthleteReport"] = "Informe atleta",
        ["Report_SessionHeader"] = "Cabecera de sesión",
        ["Report_TrainingSummary"] = "Resumen entrenamiento",
        ["Report_GroupConsistency"] = "Consistencia grupo",
        ["Report_MinMaxCharts"] = "Gráficos Min-Max",
        ["Report_TimeTables"] = "Tablas de tiempos",
        ["Report_IndConsistency"] = "Consistencia individual",
        ["Report_SplitAnalysis"] = "Análisis de parciales",
        ["Report_Ranking"] = "Ranking",
        ["Report_Penalties"] = "Penalizaciones",
        ["Report_AthleteProfile"] = "Perfil del atleta",

        // ── Context menu: library ────────────────────────────────
        ["CtxLib_CreateNew"] = "Crear nueva biblioteca...",
        ["CtxLib_Import"] = "Importar otra biblioteca...",
        ["CtxLib_ClearCache"] = "Vaciar caché y recargar",
        ["CtxLib_Merge"] = "Combinar con otra biblioteca...",

        // ── Context menu: sessions ───────────────────────────────
        ["CtxSes_NewSession"] = "Nueva sesión",
        ["CtxSes_ImportCrown"] = "Importar desde .crown",
        ["CtxSes_NewFromFiles"] = "Nueva sesión desde archivos de video",

        // ── Context menu: session row ────────────────────────────
        ["CtxRow_Edit"] = "Editar...",
        ["CtxRow_Customize"] = "Personalizar icono y color...",
        ["CtxRow_Export"] = "Exportar",
        ["CtxRow_AddFavorites"] = "Agregar a favoritos",
        ["CtxRow_Delete"] = "Eliminar sesión",

        // ── Context menu: remote session ─────────────────────────
        ["CtxRemSes_AddToPersonal"] = "Añadir a biblioteca personal",
        ["CtxRemSes_Delete"] = "Eliminar sesión",
        // ── Context menu: smart folder ──────────────────────
        ["CtxSF_Edit"] = "Editar...",
        ["CtxSF_Customize"] = "Personalizar icono y color...",
        ["CtxSF_Delete"] = "Eliminar carpeta",
        // ── Context menu: local video ────────────────────────────
        ["CtxVid_OpenPlayer"] = "Abrir en reproductor",
        ["CtxVid_Share"] = "Compartir",
        ["CtxVid_AddFavorites"] = "Añadir a Favoritos",
        ["CtxVid_DownloadOffline"] = "Descargar (disponible offline)",
        ["CtxVid_LeaveCloud"] = "Dejar en la nube",
        ["CtxVid_Delete"] = "Eliminar vídeo",

        // ── Context menu: remote video ───────────────────────────
        ["CtxRemVid_Open"] = "Abrir",
        ["CtxRemVid_AddToLibrary"] = "+ Añadir a mi biblioteca",
        ["CtxRemVid_Favorites"] = "Favoritos",
        ["CtxRemVid_Share"] = "Compartir",
        ["CtxRemVid_Delete"] = "Eliminar",

        // ── Login ────────────────────────────────────────────────
        ["Login_AccessRequired"] = "Acceso requerido",
        ["Login_Subtitle"] = "Inicia sesión con tu cuenta del equipo para continuar.",
        ["Login_EmailPH"] = "Email",
        ["Login_PasswordPH"] = "Contraseña",
        ["Login_SignIn"] = "Iniciar sesión",

        // ── Common ───────────────────────────────────────────────
        ["Common_Select"] = "Seleccionar",
        ["Common_All"] = "Todos",
        ["Common_Edit"] = "Editar",
        ["Common_EditEllipsis"] = "Editar...",
        ["Common_Share"] = "Compartir",
        ["Common_Delete"] = "Eliminar",
        ["Common_Cancel"] = "Cancelar",
        ["Common_Apply"] = "Aplicar",
        ["Common_Save"] = "Guardar",
        ["Common_Add"] = "Añadir",
        ["Common_Clear"] = "Limpiar",
        ["Common_Name"] = "Nombre",
        ["Common_Place"] = "Lugar",
        ["Common_Open"] = "Abrir",
        ["Common_Favorites"] = "Favoritos",
        ["Common_Customize"] = "Personalizar...",

        // ── StringFormats ────────────────────────────────────────
        ["Format_VideosNamed"] = "{0} vídeos con nombre asignado",
        ["Format_MatchCount"] = "Coinciden: {0}",
        ["Format_ModifyCount"] = "Se modificarán {0} vídeos",
        ["Format_Selected"] = "{0} seleccionados",
        ["Format_VideosSelected"] = "{0} vídeo(s) seleccionado(s)",
    };

    // ═══════════════════════════════════════════════════════════════
    //  ENGLISH
    // ═══════════════════════════════════════════════════════════════
    private static readonly Dictionary<string, string> _en = new()
    {
        // ── Sidebar ──────────────────────────────────────────────
        ["Sidebar_OrgLibrary"] = "Organization Library",
        ["Sidebar_PersonalLibrary"] = "Personal Library",
        ["Sidebar_GeneralGallery"] = "General Gallery",
        ["Sidebar_Sessions"] = "Sessions",
        ["Sidebar_VideoLessons"] = "Video Lessons",
        ["Sidebar_Favorites"] = "Favorites",
        ["Sidebar_Videos"] = "Videos",
        ["Sidebar_Trash"] = "Trash",
        ["Sidebar_SmartFolder"] = "Smart Folder",
        ["Sidebar_Diary"] = "Diary",

        // ── Main content ─────────────────────────────────────────
        ["Main_SelectAll"] = "Select all",
        ["Main_SelectSession"] = "Select a session",
        ["Main_SessionVideosHere"] = "Session videos will appear here",

        // ── Right panel titles ───────────────────────────────────
        ["RightPanel_ActionsAnalysis"] = "Actions & Analysis",
        ["RightPanel_FiltersStats"] = "Filters & Statistics",
        ["RightPanel_SessionDiary"] = "Session Diary",

        // ── Batch actions (local) ────────────────────────────────
        ["Batch_BatchActions"] = "Batch Actions",
        ["Batch_VideoSelection"] = "Video Selection",
        ["Batch_Select"] = "Select",
        ["Batch_All"] = "All",
        ["Batch_Playlist"] = "Playlist",
        ["Batch_Edit"] = "Edit",
        ["Batch_Share"] = "Share",
        ["Batch_Delete"] = "Delete",

        // ── Batch actions (remote) ───────────────────────────────
        ["Batch_RemoteVideoSelection"] = "Remote Video Selection",
        ["Batch_DownloadToPersonal"] = "Download to Personal",
        ["Batch_AddToPersonal"] = "Add to Personal",
        ["Batch_RemovePersonal"] = "Remove from Personal",
        ["Batch_DeleteCloud"] = "Delete from Cloud",

        // ── Analysis ─────────────────────────────────────────────
        ["Analysis_TechAnalysis"] = "Technical Analysis",
        ["Analysis_QuickAnalysis"] = "Quick Analysis",
        ["Analysis_IsolatedMode"] = "Isolated Mode",
        ["Analysis_Clean"] = "Clean",

        // ── Filters ────────────────────────────────────────────
        ["Filter_Filters"] = "Filters",
        ["Filter_Clean"] = "Clean",
        ["Filter_Place"] = "Place",
        ["Filter_Athlete"] = "Athlete",
        ["Filter_Section"] = "Section",
        ["Filter_Tag"] = "Tag",
        ["Filter_DateFrom"] = "Date from",
        ["Filter_DateTo"] = "Date to",

        // ── Stats ────────────────────────────────────────────────
        ["Stats_DataStats"] = "Data & Statistics",
        ["Stats_Videos"] = "Videos",
        ["Stats_Time"] = "Time",
        ["Stats_Sessions"] = "Sessions",

        // ── Split times ──────────────────────────────────────────
        ["SplitTimes_BySection"] = "⏱ Split Times by Section",
        ["SplitTimes_Open"] = "Open",
        ["SplitTimes_Times"] = "Times",
        ["SplitTimes_VsYou"] = "vs You",

        // ── Table headers ────────────────────────────────────────
        ["Table_Athlete"] = "ATHLETE",
        ["Table_Category"] = "CAT.",
        ["Table_Time"] = "TIME",
        ["Table_Penalty"] = "PENAL.",
        ["Table_Total"] = "TOTAL",
        ["Table_VsYou"] = "vs YOU",

        // ── Tag stats ────────────────────────────────────────────
        ["TagStats_TaggedNamed"] = "Tagged/Named Videos",
        ["TagStats_EventTags"] = "Event Tags",
        ["TagStats_TotalEvents"] = "total events",
        ["TagStats_UniqueTypes"] = "unique types",
        ["TagStats_MostUsedEvents"] = "Most Used Events",
        ["TagStats_Labels"] = "Labels",
        ["TagStats_Assigned"] = "assigned",
        ["TagStats_Unique"] = "unique",
        ["TagStats_PerSession"] = "per session",
        ["TagStats_MostUsedLabels"] = "Most Used Labels",

        // ── Recording session popup ──────────────────────────────
        ["RecPopup_Title"] = "New Recording Session",
        ["RecPopup_Subtitle"] = "Create a session and record videos directly",
        ["RecPopup_Place"] = "Place *",
        ["RecPopup_PlacePH"] = "E.g.: Olympic canal",
        ["RecPopup_SessionName"] = "Session Name *",
        ["RecPopup_SessionNamePH"] = "E.g.: Morning training",
        ["RecPopup_SessionType"] = "Session type * (quarters, halves, long, etc.)",
        ["RecPopup_CreateRecord"] = "Create & Record",

        // ── Smart folder popup ───────────────────────────────────        ["SmartFolder_Name"] = "Name",
        ["SmartFolder_NamePlaceholder"] = "Folder name",        ["SmartFolder_Subtitle"] = "Define criteria to group videos automatically",
        ["SmartFolder_Conditions"] = "Conditions",
        ["SmartFolder_And"] = "AND",
        ["SmartFolder_Or"] = "OR",
        ["SmartFolder_ValuePH"] = "Value",
        ["SmartFolder_SecondDatePH"] = "Second value (date)",
        ["SmartFolder_AddCondition"] = "Add condition",

        // ── Color / Icon popup ───────────────────────────────────
        ["Popup_Color"] = "Color",
        ["Popup_Icon"] = "Icon",

        // ── Batch edit popup ─────────────────────────────────────
        ["BatchEdit_Title"] = "Edit Selected Videos",
        ["BatchEdit_AssignAthlete"] = "Assign Athlete",
        ["BatchEdit_LastNamePH"] = "Last Name",
        ["BatchEdit_FirstNamePH"] = "First Name",
        ["BatchEdit_NoAthletes"] = "No athletes available",
        ["BatchEdit_AssignSection"] = "Assign Section/Segment",
        ["BatchEdit_Range"] = "(1-99)",
        ["BatchEdit_AddTags"] = "Add Tags",
        ["BatchEdit_NewTagPH"] = "New tag",
        ["BatchEdit_NoTags"] = "No tags available",

        // ── Edit session popup ───────────────────────────────────
        ["EditSession_Title"] = "Edit Session",
        ["EditSession_Name"] = "Name",
        ["EditSession_NamePH"] = "Session name",
        ["EditSession_Place"] = "Place",
        ["EditSession_PlacePH"] = "Session place",
        ["EditSession_SessionType"] = "Session Type",
        ["EditSession_SessionTypePH"] = "Session type",

        // ── Split times popup (detail) ───────────────────────────
        ["SplitPopup_Title"] = "Split Times by Section",
        ["SplitPopup_Sections"] = "Sections",
        ["SplitPopup_HTML"] = "HTML",
        ["SplitPopup_PDF"] = "PDF",
        ["SplitPopup_Legend"] = "Lap = split time · Acum. = accumulated time · CV% = Coefficient of Variation = (σ / x̄) × 100 (lower = more consistent)",

        // ── Report sections popup ────────────────────────────────
        ["Report_Title"] = "Report Sections",
        ["Report_QuickSummary"] = "Quick Summary",
        ["Report_FullAnalysis"] = "Full Analysis",
        ["Report_AthleteReport"] = "Athlete Report",
        ["Report_SessionHeader"] = "Session Header",
        ["Report_TrainingSummary"] = "Training Summary",
        ["Report_GroupConsistency"] = "Group Consistency",
        ["Report_MinMaxCharts"] = "Min-Max Charts",
        ["Report_TimeTables"] = "Time Tables",
        ["Report_IndConsistency"] = "Individual Consistency",
        ["Report_SplitAnalysis"] = "Split Analysis",
        ["Report_Ranking"] = "Ranking",
        ["Report_Penalties"] = "Penalties",
        ["Report_AthleteProfile"] = "Athlete Profile",

        // ── Context menu: library ────────────────────────────────
        ["CtxLib_CreateNew"] = "Create New Library...",
        ["CtxLib_Import"] = "Import Another Library...",
        ["CtxLib_ClearCache"] = "Clear Cache and Reload",
        ["CtxLib_Merge"] = "Merge with Another Library...",

        // ── Context menu: sessions ───────────────────────────────
        ["CtxSes_NewSession"] = "New Session",
        ["CtxSes_ImportCrown"] = "Import from .crown",
        ["CtxSes_NewFromFiles"] = "New Session from Video Files",

        // ── Context menu: session row ────────────────────────────
        ["CtxRow_Edit"] = "Edit...",
        ["CtxRow_Customize"] = "Customize icon and color...",
        ["CtxRow_Export"] = "Export",
        ["CtxRow_AddFavorites"] = "Add to Favorites",
        ["CtxRow_Delete"] = "Delete session",

        // ── Context menu: remote session ─────────────────────────
        ["CtxRemSes_AddToPersonal"] = "Add to Personal Library",
        ["CtxRemSes_Delete"] = "Delete Session",
        // ── Context menu: smart folder ──────────────────────
        ["CtxSF_Edit"] = "Edit...",
        ["CtxSF_Customize"] = "Customize icon and color...",
        ["CtxSF_Delete"] = "Delete folder",
        // ── Context menu: local video ────────────────────────────
        ["CtxVid_OpenPlayer"] = "Open in Player",
        ["CtxVid_Share"] = "Share",
        ["CtxVid_AddFavorites"] = "Add to Favorites",
        ["CtxVid_DownloadOffline"] = "Download (available offline)",
        ["CtxVid_LeaveCloud"] = "Leave in Cloud",
        ["CtxVid_Delete"] = "Delete video",

        // ── Context menu: remote video ───────────────────────────
        ["CtxRemVid_Open"] = "Open",
        ["CtxRemVid_AddToLibrary"] = "+ Add to My Library",
        ["CtxRemVid_Favorites"] = "Favorites",
        ["CtxRemVid_Share"] = "Share",
        ["CtxRemVid_Delete"] = "Delete",

        // ── Login ────────────────────────────────────────────────
        ["Login_AccessRequired"] = "Access Required",
        ["Login_Subtitle"] = "Log in with your team account to continue.",
        ["Login_EmailPH"] = "Email",
        ["Login_PasswordPH"] = "Password",
        ["Login_SignIn"] = "Sign In",

        // ── Common ───────────────────────────────────────────────
        ["Common_Select"] = "Select",
        ["Common_All"] = "All",
        ["Common_Edit"] = "Edit",
        ["Common_EditEllipsis"] = "Edit...",
        ["Common_Share"] = "Share",
        ["Common_Delete"] = "Delete",
        ["Common_Cancel"] = "Cancel",
        ["Common_Apply"] = "Apply",
        ["Common_Save"] = "Save",
        ["Common_Add"] = "Add",
        ["Common_Clear"] = "Clear",
        ["Common_Name"] = "Name",
        ["Common_Place"] = "Place",
        ["Common_Open"] = "Open",
        ["Common_Favorites"] = "Favorites",
        ["Common_Customize"] = "Customize...",

        // ── StringFormats ────────────────────────────────────────
        ["Format_VideosNamed"] = "{0} videos with assigned name",
        ["Format_MatchCount"] = "Matches: {0}",
        ["Format_ModifyCount"] = "{0} videos will be modified",
        ["Format_Selected"] = "{0} selected",
        ["Format_VideosSelected"] = "{0} video(s) selected",
    };
}
