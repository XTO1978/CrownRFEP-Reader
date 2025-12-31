using Foundation;
using UIKit;
using ObjCRuntime;

namespace CrownRFEP_Reader.Platforms.MacCatalyst;

/// <summary>
/// Clase helper para eliminar el highlight de selección nativo en UICollectionView.
/// MacCatalyst muestra un recuadro gris claro alrededor de las celdas seleccionadas
/// que interfiere con nuestros estilos personalizados.
/// </summary>
public static class CollectionViewSelectionFix
{
    /// <summary>
    /// Configura un UICollectionView para no mostrar highlight de selección nativo.
    /// </summary>
    public static void DisableNativeSelectionHighlight(UICollectionView collectionView)
    {
        if (collectionView == null) return;

        // Configuración básica
        collectionView.BackgroundColor = UIColor.Clear;
        
        // Observar cambios en las celdas visibles para eliminar su highlight
        // Esto se ejecuta cuando las celdas se reciclan o se muestran
        var observer = collectionView.AddObserver(
            "visibleCells", 
            NSKeyValueObservingOptions.New, 
            (change) => ClearCellBackgrounds(collectionView));
    }

    /// <summary>
    /// Limpia los backgrounds de selección de todas las celdas visibles.
    /// </summary>
    public static void ClearCellBackgrounds(UICollectionView collectionView)
    {
        if (collectionView?.VisibleCells == null) return;

        foreach (var cell in collectionView.VisibleCells)
        {
            // Eliminar el view de background de selección
            cell.SelectedBackgroundView = null;
            cell.BackgroundView = null;
            cell.BackgroundColor = UIColor.Clear;
            cell.ContentView.BackgroundColor = UIColor.Clear;
            
            // Desactivar el estado de highlight
            cell.Highlighted = false;
        }
    }
}
