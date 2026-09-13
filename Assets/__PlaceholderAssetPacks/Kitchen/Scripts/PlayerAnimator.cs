using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerAnimator : NetworkBehaviour
{
    private static readonly int IsWalking = Animator.StringToHash(IS_WALKING);
    private const string IS_WALKING = "IsWalking";
    
    [SerializeField] private Player player;
    
    private Animator animator;
    
    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    private void Update()
    {
        if (!IsOwner) return;
        animator.SetBool(IsWalking, player.IsWalking());
    }
}