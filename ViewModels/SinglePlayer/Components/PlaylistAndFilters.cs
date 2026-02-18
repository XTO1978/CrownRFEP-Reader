using CrownRFEP_Reader.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace CrownRFEP_Reader.ViewModels.SinglePlayer.Components;

internal sealed class PlaylistAndFilters
{
    public List<VideoClip> SessionVideos { get; private set; } = new();
    public List<VideoClip> FilteredPlaylist { get; private set; } = new();
    public int CurrentPlaylistIndex { get; private set; }
    public bool ShowFilters { get; set; }

    public ObservableCollection<FilterOption<Athlete>> AthleteOptions { get; } = new();
    public ObservableCollection<FilterOption<int>> SectionOptions { get; } = new();
    public ObservableCollection<FilterOption<Category>> CategoryOptions { get; } = new();

    public FilterOption<Athlete>? SelectedAthlete { get; set; }
    public FilterOption<int>? SelectedSection { get; set; }
    public FilterOption<Category>? SelectedCategory { get; set; }

    public int PlaylistCount => FilteredPlaylist.Count;
    public bool CanGoPrevious => CurrentPlaylistIndex > 0;
    public bool CanGoNext => CurrentPlaylistIndex < FilteredPlaylist.Count - 1;
    public bool HasPlaylist => FilteredPlaylist.Count > 1;

    public void SetSessionVideos(List<VideoClip> sessionVideos)
    {
        SessionVideos = sessionVideos ?? new List<VideoClip>();
    }

    public void InitializeWithPlaylist(List<VideoClip> playlist, int startIndex)
    {
        SessionVideos = playlist ?? new List<VideoClip>();
        FilteredPlaylist = SessionVideos.OrderBy(v => v.CreationDate).ToList();
        CurrentPlaylistIndex = Math.Max(0, Math.Min(startIndex, FilteredPlaylist.Count - 1));
    }

    public void SetCurrentPlaylistIndex(int index)
    {
        CurrentPlaylistIndex = index;
    }

    public int FindFilteredIndexByVideoId(int videoId)
        => FilteredPlaylist.FindIndex(v => v.Id == videoId);

    public int FindSessionIndexByVideoId(int videoId)
        => SessionVideos.FindIndex(v => v.Id == videoId);

    public VideoClip? GetCurrentPlaylistVideo()
    {
        if (CurrentPlaylistIndex < 0 || CurrentPlaylistIndex >= FilteredPlaylist.Count)
            return null;

        return FilteredPlaylist[CurrentPlaylistIndex];
    }

    public bool TryMovePrevious()
    {
        if (!CanGoPrevious)
            return false;

        CurrentPlaylistIndex--;
        return true;
    }

    public bool TryMoveNext()
    {
        if (!CanGoNext)
            return false;

        CurrentPlaylistIndex++;
        return true;
    }

    public void SetAthleteOptions(ObservableCollection<FilterOption<Athlete>> options)
    {
        AthleteOptions.Clear();
        if (options == null) return;
        foreach (var option in options)
            AthleteOptions.Add(option);
    }

    public void SetSectionOptions(ObservableCollection<FilterOption<int>> options)
    {
        SectionOptions.Clear();
        if (options == null) return;
        foreach (var option in options)
            SectionOptions.Add(option);
    }

    public void SetCategoryOptions(ObservableCollection<FilterOption<Category>> options)
    {
        CategoryOptions.Clear();
        if (options == null) return;
        foreach (var option in options)
            CategoryOptions.Add(option);
    }

    public void PopulateFilterOptions(List<Category> allCategories)
    {
        // Opción "Todos" para cada filtro
        AthleteOptions.Clear();
        AthleteOptions.Add(new FilterOption<Athlete>(null, "Todos los atletas"));

        SectionOptions.Clear();
        SectionOptions.Add(new FilterOption<int>(0, "Todas las secciones"));

        CategoryOptions.Clear();
        CategoryOptions.Add(new FilterOption<Category>(null, "Todas las categorías"));

        // Atletas únicos
        var uniqueAthletes = SessionVideos
            .Where(v => v.Atleta != null)
            .Select(v => v.Atleta!)
            .DistinctBy(a => a.Id)
            .OrderBy(a => a.NombreCompleto);

        foreach (var athlete in uniqueAthletes)
        {
            AthleteOptions.Add(new FilterOption<Athlete>(athlete, athlete.NombreCompleto ?? $"Atleta {athlete.Id}"));
        }

        // Secciones únicas
        var uniqueSections = SessionVideos
            .Select(v => v.Section)
            .Distinct()
            .OrderBy(s => s);

        foreach (var section in uniqueSections)
        {
            SectionOptions.Add(new FilterOption<int>(section, $"Sección {section}"));
        }

        // Categorías únicas (basadas en los atletas de la sesión)
        var usedCategoryIds = SessionVideos
            .Where(v => v.Atleta != null)
            .Select(v => v.Atleta!.CategoriaId)
            .Distinct()
            .ToHashSet();

        var usedCategories = allCategories
            .Where(c => usedCategoryIds.Contains(c.Id))
            .OrderBy(c => c.NombreCategoria);

        foreach (var category in usedCategories)
        {
            CategoryOptions.Add(new FilterOption<Category>(category, category.NombreCategoria ?? $"Categoría {category.Id}"));
        }

        // Seleccionar "Todos" por defecto
        SelectedAthlete = AthleteOptions.FirstOrDefault();
        SelectedSection = SectionOptions.FirstOrDefault();
        SelectedCategory = CategoryOptions.FirstOrDefault();
    }

    public bool ApplyFilters(VideoClip? currentVideo, out bool shouldNavigateToFirst)
    {
        shouldNavigateToFirst = false;
        var filtered = SessionVideos.AsEnumerable();

        // Filtrar por atleta
        if (SelectedAthlete?.Value != null)
        {
            filtered = filtered.Where(v => v.AtletaId == SelectedAthlete.Value.Id);
        }

        // Filtrar por sección
        if (SelectedSection?.Value > 0)
        {
            filtered = filtered.Where(v => v.Section == SelectedSection.Value);
        }

        // Filtrar por categoría
        if (SelectedCategory?.Value != null)
        {
            filtered = filtered.Where(v => v.Atleta?.CategoriaId == SelectedCategory.Value.Id);
        }

        FilteredPlaylist = filtered.OrderBy(v => v.CreationDate).ToList();

        // Actualizar índice actual
        if (currentVideo != null)
        {
            CurrentPlaylistIndex = FilteredPlaylist.FindIndex(v => v.Id == currentVideo.Id);
            if (CurrentPlaylistIndex < 0 && FilteredPlaylist.Count > 0)
            {
                // El video actual no está en la playlist filtrada, ir al primero
                CurrentPlaylistIndex = 0;
                shouldNavigateToFirst = true;
            }
        }

        return true;
    }

    public void ClearFilters()
    {
        SelectedAthlete = AthleteOptions.FirstOrDefault();
        SelectedSection = SectionOptions.FirstOrDefault();
        SelectedCategory = CategoryOptions.FirstOrDefault();
    }
}
