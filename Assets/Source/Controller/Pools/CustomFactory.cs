using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Spawn RandomModel object class from pool and place 3D model class CustomItemVisualModel inside it. 
/// </summary>
public class CustomFactory : Singleton<CustomFactory>
{
//     [SerializeField] private CustomItemModelContainerSO modelContainer;
//
//     public T SpawnObject<T>(ItemType type)
//     {
//         CustomItem item = PoolFactory.Instance.GetDeactiveItem<CustomItem>(PoolEnum.CustomItem);
//         CustomItemVisualModel visualModel = Instantiate(GetPrefabByType(type));
//         item.OnInitialize(visualModel);
//         item.SetActiveGameObject(true);
//         return (T)((object)item);
//     }
//
//     private CustomItem GetPrefabByType(ItemType type)
//     {
//         return modelContainer.products.FirstOrDefault(item => item.type == type)?.prefab;
//     }
}