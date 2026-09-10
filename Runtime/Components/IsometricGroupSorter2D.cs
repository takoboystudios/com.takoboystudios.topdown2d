using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

namespace TakoBoyStudios.TopDown2D
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(SortingGroup))]
    public class IsometricGroupSorter2D : MonoBehaviour
    {
        const float PpuScaler = 10;

        [BoxGroup("Settings")]
        [Tooltip("Offset for sprites with bottom pivot. Positive moves sort point down.")]
        public int bottomPivotOffset;

        [BoxGroup("Settings")]
        [Tooltip("Pixels Per Unit - should match your sprite import settings.")]
        public int pixelsPerUnit = 1;

        [BoxGroup("Tilemap Integration")]
        [InfoBox(
            "Assign tilemap to match Unity's Individual tile sorting. Leave empty for world-space sorting."
        )]
        [SerializeField]
        Tilemap referenceTilemap;

        [BoxGroup("Debug")]
        [ShowInInspector]
        [DisplayAsString]
        [ShowIf("@referenceTilemap != null")]
        string DebugInfo
        {
            get
            {
                if (referenceTilemap == null)
                    return "";
                Vector3Int cell = referenceTilemap.WorldToCell(transform.position);
                int baseSorting = -(cell.x + cell.y);
                TilemapRenderer rend = referenceTilemap.GetComponent<TilemapRenderer>();
                int tilemapBase = rend != null ? rend.sortingOrder : 0;
                return $"Cell: {cell} | Base: {baseSorting} | Tilemap Base: {tilemapBase}";
            }
        }

        SortingGroup _sortingGroup;
        TilemapRenderer _tilemapRenderer;
        int _lastSortOrder;
        float _sortingPrecision;

        void Awake()
        {
            _sortingGroup = GetComponent<SortingGroup>();

            if (_sortingGroup == null)
            {
                Debug.LogError(
                    $"IsometricGroupSorter2D on {gameObject.name} requires a SortingGroup component!",
                    this
                );
                enabled = false;
                return;
            }

            if (referenceTilemap != null)
            {
                _tilemapRenderer = referenceTilemap.GetComponent<TilemapRenderer>();
            }

            _sortingPrecision = pixelsPerUnit * PpuScaler;
            UpdateSortingOrder();
        }

        private Vector3Int _lastCell;

        void LateUpdate()
        {
            if (referenceTilemap == null)
                return;

            Vector2 updatedPosition = transform.position + new Vector3(0, bottomPivotOffset);
            Vector3Int currentCell = referenceTilemap.WorldToCell(updatedPosition);

            // Clear last cell
            if (_lastCell != currentCell)
            {
                referenceTilemap.SetTileFlags(_lastCell, TileFlags.None);
                referenceTilemap.SetColor(_lastCell, Color.white);
                _lastCell = currentCell;
            }

            referenceTilemap.SetTileFlags(currentCell, TileFlags.None);
            referenceTilemap.SetColor(currentCell, Color.magenta);
            TileBase tile = referenceTilemap.GetTile(currentCell);

            UpdateSortingOrder();
        }

        void UpdateSortingOrder()
        {
            int newSortOrder;

            if (referenceTilemap != null)
            {
                // TILEMAP MODE: Match Unity's tile sorting

                Vector2 updatedPosition = transform.position + new Vector3(0, bottomPivotOffset);
                // Get cell position
                Vector3Int cell = referenceTilemap.WorldToCell(updatedPosition);

                // Unity's formula for TopRight: -(cellX + cellY)
                int cellBasedSorting = -(cell.x + cell.y);

                // Get tilemap's base sorting order
                int tilemapBaseSorting =
                    _tilemapRenderer != null ? _tilemapRenderer.sortingOrder : 0;

                // Calculate sub-cell offset for fine sorting
                // Vector3 cellCenter = referenceTilemap.GetCellCenterWorld(cell);
                // float yOffsetInCell = transform.position.y - cellCenter.y;
                //
                // // Scale: We want sub-cell precision but not too extreme
                // // Tiles are typically spaced by 1 unit of sorting, so we use 100 for sub-cell
                // int subCellSort = (int)(yOffsetInCell * 100);
                //
                // // Pivot offset (scaled similarly)
                // int pivotSort = (int)(bottomPivotOffset * 10);

                // Final calculation:
                // Match tilemap base + cell-based sorting + sub-cell precision
                newSortOrder = tilemapBaseSorting + cellBasedSorting;
            }
            else
            {
                // WORLD MODE: Original world Y-based sorting
                newSortOrder = (int)(
                    (-transform.position.y + (bottomPivotOffset * 0.5f)) * _sortingPrecision
                );
            }

            // Only update if changed
            if (newSortOrder != _lastSortOrder)
            {
                _sortingGroup.sortingOrder = newSortOrder;
                _lastSortOrder = newSortOrder;
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (_sortingGroup == null)
                _sortingGroup = GetComponent<SortingGroup>();

            if (referenceTilemap != null && _tilemapRenderer == null)
                _tilemapRenderer = referenceTilemap.GetComponent<TilemapRenderer>();

            _sortingPrecision = pixelsPerUnit * PpuScaler;

            if (_sortingGroup != null)
                UpdateSortingOrder();
        }
#endif
    }
}
