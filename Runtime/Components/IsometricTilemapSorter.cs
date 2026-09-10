using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// Makes tilemap tiles sort individually with IsometricGroupSorter2D objects.
    /// Objects can now go in front of some tiles and behind others!
    /// </summary>
    [ExecuteInEditMode]
    [RequireComponent(typeof(TilemapRenderer))]
    public class IsometricTilemapSorter : MonoBehaviour
    {
        [BoxGroup("Settings")]
        [InfoBox(
            "Individual mode makes EACH tile sort based on its Y position.\nObjects can go between tiles dynamically!",
            InfoMessageType.Info
        )]
        [Tooltip("Enable to make each tile sort independently (performance cost).")]
        [SerializeField]
        bool useIndividualMode = true;

        [BoxGroup("Settings")]
        [ShowIf("useIndividualMode")]
        [Tooltip("Sorting mode for individual tiles.")]
        [SerializeField]
        TilemapRenderer.SortOrder sortOrder = TilemapRenderer.SortOrder.TopRight;

        [BoxGroup("Settings")]
        [Tooltip("Chunk size for rendering (larger = better performance, less granular sorting).")]
        [ShowIf("@!useIndividualMode")]
        [SerializeField]
        Vector3Int chunkSize = new Vector3Int(32, 32, 32);

        [BoxGroup("Info")]
        [ShowInInspector]
        [DisplayAsString]
        [PropertyOrder(10)]
        string CurrentMode =>
            _tilemapRenderer != null ? $"Mode: {_tilemapRenderer.mode}" : "No TilemapRenderer";

        [BoxGroup("Info")]
        [ShowInInspector]
        [DisplayAsString]
        [PropertyOrder(11)]
        string SortingInfo =>
            _tilemapRenderer != null ? $"Sort Order: {_tilemapRenderer.sortOrder}" : "N/A";

        TilemapRenderer _tilemapRenderer;

        void Awake()
        {
            _tilemapRenderer = GetComponent<TilemapRenderer>();

            if (_tilemapRenderer == null)
            {
                Debug.LogError(
                    $"IsometricTilemapPerTileSorter on {gameObject.name} requires a TilemapRenderer!",
                    this
                );
                enabled = false;
                return;
            }

            ApplySettings();
        }

        void ApplySettings()
        {
            if (_tilemapRenderer == null)
                return;

            if (useIndividualMode)
            {
                // Individual mode: each tile sorts independently
                _tilemapRenderer.mode = TilemapRenderer.Mode.Individual;
                _tilemapRenderer.sortOrder = sortOrder;

                Debug.Log(
                    $"Tilemap {gameObject.name} set to Individual mode. "
                        + $"Each tile now sorts based on its Y position!",
                    this
                );
            }
            else
            {
                // Chunk mode: tiles render in chunks (better performance)
                _tilemapRenderer.mode = TilemapRenderer.Mode.Chunk;
                _tilemapRenderer.chunkSize = chunkSize;

                Debug.Log(
                    $"Tilemap {gameObject.name} set to Chunk mode ({chunkSize}). "
                        + $"Better performance but less precise sorting.",
                    this
                );
            }
        }

        [BoxGroup("Actions")]
        [Button("Apply Settings Now", ButtonSizes.Large)]
        [PropertyOrder(20)]
        void ForceApplySettings()
        {
            if (_tilemapRenderer == null)
                _tilemapRenderer = GetComponent<TilemapRenderer>();

            ApplySettings();
        }

        [BoxGroup("Actions")]
        [Button("Test: Switch to Individual Mode", ButtonSizes.Medium)]
        [PropertyOrder(21)]
        [HideIf("useIndividualMode")]
        void SwitchToIndividual()
        {
            useIndividualMode = true;
            ApplySettings();
        }

        [BoxGroup("Actions")]
        [Button("Test: Switch to Chunk Mode", ButtonSizes.Medium)]
        [PropertyOrder(22)]
        [ShowIf("useIndividualMode")]
        void SwitchToChunk()
        {
            useIndividualMode = false;
            ApplySettings();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (_tilemapRenderer == null)
                _tilemapRenderer = GetComponent<TilemapRenderer>();

            if (_tilemapRenderer != null)
                ApplySettings();
        }
#endif
    }
}
