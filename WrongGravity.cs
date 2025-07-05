using GlobalEnums;
using Modding;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vasi;
namespace WrongGravity
{
	public class isFlipped:MonoBehaviour{public bool flipped=false;}
	public class WrongGravity:Mod
	{
		private bool flipped=false;
		public override string GetVersion()=>VersionUtil.GetVersion<WrongGravity>();
		public override void Initialize()
		{
			On.tk2dCamera.UpdateCameraMatrix+=UpdateCameraMatrix;
			On.GameCameras.StartScene+=StartScene;
			On.HeroController.Awake+=Awake;
			On.HeroController.Update+=Update;
			On.HeroController.FixedUpdate+=FixedUpdate;
			On.HeroController.Move+=Move;
			On.HeroController.Jump+=Jump;
			On.HeroController.DoubleJump+=DoubleJump;
			On.HeroController.Dash+=Dash;
			On.HeroController.CancelHeroJump+=CancelHeroJump;
			On.HeroController.TakeDamage+=TakeDamage;
			On.HeroController.BounceHigh+=BounceHigh;
			On.HeroController.ShroomBounce+=ShroomBounce;
			On.HeroController.RecoilLeft+=RecoilLeft;
			On.HeroController.RecoilRight+=RecoilRight;
			On.HeroController.RecoilRightLong+=RecoilRightLong;
			On.HeroController.RecoilLeftLong+=RecoilLeftLong;
			On.HeroController.RecoilDown+=RecoilDown;
			On.HeroController.CanDreamNail+=CanDreamNail;
//			On.HeroController.CanInteract+=CanInteract;
			On.HeroController.AffectedByGravity+=AffectedByGravity;
			On.HeroController.FallCheck+=FallCheck;
			On.HeroController.JumpReleased+=JumpReleased;
			On.HeroController.CheckStillTouchingWall+=CheckStillTouchingWall;
			On.HeroController.CheckForBump+=CheckForBump;
			On.HeroController.CheckNearRoof+=CheckNearRoof;
			On.HeroController.CheckTouchingGround+=CheckTouchingGround;
			On.HeroController.CheckTouching+=CheckTouching;
			On.HeroController.CheckTouchingAdvanced+=CheckTouchingAdvanced;
			On.HeroController.FindCollisionDirection+=FindCollisionDirection;
			On.HeroController.OnCollisionEnter2D+=OnCollisionEnter2D;
			On.HeroController.SetupGameRefs+=SetupGameRefs;
			On.HeroController.FindGroundPoint+=FindGroundPoint;
			On.HeroController.FindGroundPointY+=FindGroundPointY;
			On.TransitionPoint.GetGatePosition+=GetGatePosition;
		}
		private void UpdateCameraMatrix(On.tk2dCamera.orig_UpdateCameraMatrix orig,tk2dCamera self)
		{
			orig(self);
			if(!GameManager.instance.IsGameplayScene()||GameCameras.instance==null||GameCameras.instance.tk2dCam==null)
				return;
			UnityEngine.Camera c=self.GetComponent<UnityEngine.Camera>();
			if(c==null)
				return;
			c.projectionMatrix*=Matrix4x4.Scale(new Vector3(1,-1,1));
		}
		private void StartScene(On.GameCameras.orig_StartScene orig,GameCameras self)
		{
			orig(self);
			foreach(UnityEngine.Camera c in self.tk2dCam.GetComponentsInChildren<UnityEngine.Camera>())
			{
				if(c.gameObject.GetComponent<isFlipped>()==null)
					c.gameObject.AddComponent<isFlipped>();
				if(c.gameObject.GetComponent<isFlipped>().flipped)
					return;
				c.projectionMatrix*=Matrix4x4.Scale(new Vector3(1,-1,1));
				c.gameObject.GetComponent<isFlipped>().flipped=true;
			}
		}
		private void Flip(HeroController self)
		{
			flipped=!flipped;
			Vector2 v=Mirror.GetField<HeroController,Rigidbody2D>(self,"rb2d").velocity;
			Mirror.GetFieldRef<HeroController,Rigidbody2D>(self,"rb2d").velocity=new Vector2(v.x,-v.y);
		}
		private void Awake(On.HeroController.orig_Awake orig,HeroController self)
		{
			orig(self);
			Vector3 localScale=self.transform.localScale;
			localScale.y*=-1;
			self.transform.localScale=localScale;
		}
		private void Update(On.HeroController.orig_Update orig,HeroController self)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self);
			if(f)
				Flip(self);
		}
		private void FixedUpdate(On.HeroController.orig_FixedUpdate orig,HeroController self)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self);
			if(f)
				Flip(self);
			if(!self.playerData.atBench&&Mirror.GetField<HeroController,Rigidbody2D>(self,"rb2d").gravityScale>0)
				Mirror.GetFieldRef<HeroController,Rigidbody2D>(self,"rb2d").gravityScale*=-1;
		}
		private void Move(On.HeroController.orig_Move orig,HeroController self,float move_direction)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self,move_direction);
			if(f)
				Flip(self);
		}
		private void Jump(On.HeroController.orig_Jump orig,HeroController self)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self);
			if(f)
				Flip(self);
		}
		private void DoubleJump(On.HeroController.orig_DoubleJump orig,HeroController self)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self);
			if(f)
				Flip(self);
		}
		private void Dash(On.HeroController.orig_Dash orig,HeroController self)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self);
			if(f)
				Flip(self);
		}
		private void CancelHeroJump(On.HeroController.orig_CancelHeroJump orig,HeroController self)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self);
			if(f)
				Flip(self);
		}
		public void TakeDamage(On.HeroController.orig_TakeDamage orig,HeroController self,GameObject go,CollisionSide damageSide,int damageAmount,int hazardType)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self,go,damageSide,damageAmount,hazardType);
			if(f)
				Flip(self);
		}
		private void BounceHigh(On.HeroController.orig_BounceHigh orig,HeroController self)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self);
			if(f)
				Flip(self);
		}
		private void ShroomBounce(On.HeroController.orig_ShroomBounce orig,HeroController self)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self);
			if(f)
				Flip(self);
		}
		private void RecoilLeft(On.HeroController.orig_RecoilLeft orig,HeroController self)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self);
			if(f)
				Flip(self);
		}
		private void RecoilRight(On.HeroController.orig_RecoilRight orig,HeroController self)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self);
			if(f)
				Flip(self);
		}
		private void RecoilRightLong(On.HeroController.orig_RecoilRightLong orig,HeroController self)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self);
			if(f)
				Flip(self);
		}
		private void RecoilLeftLong(On.HeroController.orig_RecoilLeftLong orig,HeroController self)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self);
			if(f)
				Flip(self);
		}
		private void RecoilDown(On.HeroController.orig_RecoilDown orig,HeroController self)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self);
			if(f)
				Flip(self);
		}
		private bool CanDreamNail(On.HeroController.orig_CanDreamNail orig,HeroController self)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self);
			bool r=orig(self);
			if(f)
				Flip(self);
			return r;
		}
		private bool CanInteract(On.HeroController.orig_CanInteract orig,HeroController self)
		{
			return self.CanInput() && self.hero_state != ActorStates.no_input && !GameManager.instance.isPaused && !self.cState.dashing && !self.cState.backDashing && !self.cState.attacking && !self.controlReqlinquished && !self.cState.hazardDeath && !self.cState.hazardRespawning && !self.cState.recoilFrozen && !self.cState.recoiling && !self.cState.transitioning && self.CheckNearRoof();
		}
		private void AffectedByGravity(On.HeroController.orig_AffectedByGravity orig,HeroController self,bool gravityApplies)
		{
			if(Mirror.GetField<HeroController,Rigidbody2D>(self,"rb2d").gravityScale<0)
				Mirror.GetFieldRef<HeroController,Rigidbody2D>(self,"rb2d").gravityScale*=-1;
			orig(self,gravityApplies);
			if(Mirror.GetField<HeroController,Rigidbody2D>(self,"rb2d").gravityScale>0)
				Mirror.GetFieldRef<HeroController,Rigidbody2D>(self,"rb2d").gravityScale*=-1;
		}
		private void FallCheck(On.HeroController.orig_FallCheck orig,HeroController self)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self);
			if(f)
				Flip(self);
		}
		private void JumpReleased(On.HeroController.orig_JumpReleased orig,HeroController self)
		{
			bool f=false;
			if(!flipped)
			{
				Flip(self);
				f=true;
			}
			orig(self);
			if(f)
				Flip(self);
		}
		private bool CheckStillTouchingWall(On.HeroController.orig_CheckStillTouchingWall orig,HeroController self,CollisionSide side, bool checkTop = false)
		{
			Collider2D col2d=Mirror.GetField<HeroController,Collider2D>(self,"col2d");
			Vector2 origin = new Vector2(col2d.bounds.min.x, col2d.bounds.min.y);
			Vector2 origin2 = new Vector2(col2d.bounds.min.x, col2d.bounds.center.y);
			Vector2 origin3 = new Vector2(col2d.bounds.min.x, col2d.bounds.max.y);
			Vector2 origin4 = new Vector2(col2d.bounds.max.x, col2d.bounds.min.y);
			Vector2 origin5 = new Vector2(col2d.bounds.max.x, col2d.bounds.center.y);
			Vector2 origin6 = new Vector2(col2d.bounds.max.x, col2d.bounds.max.y);
			float distance = 0.1f;
			RaycastHit2D raycastHit2D = default(RaycastHit2D);
			RaycastHit2D raycastHit2D2 = default(RaycastHit2D);
			RaycastHit2D raycastHit2D3 = default(RaycastHit2D);
			if (side == CollisionSide.left)
			{
				if (checkTop)
				{
					raycastHit2D = Physics2D.Raycast(origin, Vector2.left, distance, 256);
				}
				raycastHit2D2 = Physics2D.Raycast(origin2, Vector2.left, distance, 256);
				raycastHit2D3 = Physics2D.Raycast(origin3, Vector2.left, distance, 256);
			}
			else
			{
				if (side != CollisionSide.right)
				{
					Debug.LogError("Invalid CollisionSide specified.");
					return false;
				}
				if (checkTop)
				{
					raycastHit2D = Physics2D.Raycast(origin4, Vector2.right, distance, 256);
				}
				raycastHit2D2 = Physics2D.Raycast(origin5, Vector2.right, distance, 256);
				raycastHit2D3 = Physics2D.Raycast(origin6, Vector2.right, distance, 256);
			}
			if (raycastHit2D2.collider != null)
			{
				bool flag = true;
				if (raycastHit2D2.collider.isTrigger)
				{
					flag = false;
				}
				if (raycastHit2D2.collider.GetComponent<SteepSlope>() != null)
				{
					flag = false;
				}
				if (raycastHit2D2.collider.GetComponent<NonSlider>() != null)
				{
					flag = false;
				}
				if (flag)
				{
					return true;
				}
			}
			if (raycastHit2D3.collider != null)
			{
				bool flag2 = true;
				if (raycastHit2D3.collider.isTrigger)
				{
					flag2 = false;
				}
				if (raycastHit2D3.collider.GetComponent<SteepSlope>() != null)
				{
					flag2 = false;
				}
				if (raycastHit2D3.collider.GetComponent<NonSlider>() != null)
				{
					flag2 = false;
				}
				if (flag2)
				{
					return true;
				}
			}
			if (checkTop && raycastHit2D.collider != null)
			{
				bool flag3 = true;
				if (raycastHit2D.collider.isTrigger)
				{
					flag3 = false;
				}
				if (raycastHit2D.collider.GetComponent<SteepSlope>() != null)
				{
					flag3 = false;
				}
				if (raycastHit2D.collider.GetComponent<NonSlider>() != null)
				{
					flag3 = false;
				}
				if (flag3)
				{
					return true;
				}
			}
			return false;
		}
		private bool CheckForBump(On.HeroController.orig_CheckForBump orig,HeroController self,CollisionSide side)
		{
			Collider2D col2d=Mirror.GetField<HeroController,Collider2D>(self,"col2d");
			float num = 0.025f;
			float num2 = 0.2f;
			Vector2 vector = new Vector2(col2d.bounds.min.x + num2, col2d.bounds.max.y - 0.2f);
			Vector2 vector2 = new Vector2(col2d.bounds.min.x + num2, col2d.bounds.max.y + num);
			Vector2 vector3 = new Vector2(col2d.bounds.max.x - num2, col2d.bounds.max.y - 0.2f);
			Vector2 vector4 = new Vector2(col2d.bounds.max.x - num2, col2d.bounds.max.y + num);
			float num3 = 0.32f + num2;
			RaycastHit2D raycastHit2D = default(RaycastHit2D);
			RaycastHit2D raycastHit2D2 = default(RaycastHit2D);
			if (side == CollisionSide.left)
			{
				Debug.DrawLine(vector2, vector2 + Vector2.left * num3, Color.cyan, 0.15f);
				Debug.DrawLine(vector, vector + Vector2.left * num3, Color.cyan, 0.15f);
				raycastHit2D2 = Physics2D.Raycast(vector2, Vector2.left, num3, 256);
				raycastHit2D = Physics2D.Raycast(vector, Vector2.left, num3, 256);
			}
			else if (side == CollisionSide.right)
			{
				Debug.DrawLine(vector4, vector4 + Vector2.right * num3, Color.cyan, 0.15f);
				Debug.DrawLine(vector3, vector3 + Vector2.right * num3, Color.cyan, 0.15f);
				raycastHit2D2 = Physics2D.Raycast(vector4, Vector2.right, num3, 256);
				raycastHit2D = Physics2D.Raycast(vector3, Vector2.right, num3, 256);
			}
			else
			{
				Debug.LogError("Invalid CollisionSide specified.");
			}
			if (raycastHit2D2.collider != null && raycastHit2D.collider == null)
			{
				Vector2 vector5 = raycastHit2D2.point + new Vector2((side == CollisionSide.right) ? 0.1f : -0.1f, 1f);
				RaycastHit2D raycastHit2D3 = Physics2D.Raycast(vector5, Vector2.up, 1.5f, 256);
				Vector2 vector6 = raycastHit2D2.point + new Vector2((side == CollisionSide.right) ? -0.1f : 0.1f, 1f);
				RaycastHit2D raycastHit2D4 = Physics2D.Raycast(vector6, Vector2.up, 1.5f, 256);
				if (raycastHit2D3.collider != null)
				{
					Debug.DrawLine(vector5, raycastHit2D3.point, Color.cyan, 0.15f);
					if (!(raycastHit2D4.collider != null))
					{
						return true;
					}
					Debug.DrawLine(vector6, raycastHit2D4.point, Color.cyan, 0.15f);
					float num4 = raycastHit2D4.point.y - raycastHit2D3.point.y;
					if (num4 > 0f)
					{
						Debug.Log("Bump Height: " + num4.ToString());
						return true;
					}
				}
			}
			return false;
		}
		private bool CheckNearRoof(On.HeroController.orig_CheckNearRoof orig,HeroController self)
		{
			Collider2D col2d=Mirror.GetField<HeroController,Collider2D>(self,"col2d");
			Vector2 origin = new Vector2(col2d.bounds.max.x, col2d.bounds.min.y);
			Vector2 origin2 = col2d.bounds.min;
			Vector2 origin3 = new Vector2(col2d.bounds.center.x + col2d.bounds.size.x / 4f, col2d.bounds.min.y);
			Vector2 origin4 = new Vector2(col2d.bounds.center.x - col2d.bounds.size.x / 4f, col2d.bounds.min.y);
			Vector2 direction = new Vector2(-0.5f, -1f);
			Vector2 direction2 = new Vector2(0.5f, -1f);
			Vector2 up = Vector2.down;
			RaycastHit2D raycastHit2D = Physics2D.Raycast(origin2, direction, 2f, 256);
			RaycastHit2D raycastHit2D2 = Physics2D.Raycast(origin, direction2, 2f, 256);
			RaycastHit2D raycastHit2D3 = Physics2D.Raycast(origin3, up, 1f, 256);
			RaycastHit2D raycastHit2D4 = Physics2D.Raycast(origin4, up, 1f, 256);
			return raycastHit2D.collider != null || raycastHit2D2.collider != null || raycastHit2D3.collider != null || raycastHit2D4.collider != null;
		}
		private bool CheckTouchingGround(On.HeroController.orig_CheckTouchingGround orig,HeroController self)
		{
			Collider2D col2d=Mirror.GetField<HeroController,Collider2D>(self,"col2d");
			Vector2 vector = new Vector2(col2d.bounds.min.x, col2d.bounds.center.y);
			Vector2 vector2 = col2d.bounds.center;
			Vector2 vector3 = new Vector2(col2d.bounds.max.x, col2d.bounds.center.y);
			float distance = col2d.bounds.extents.y + 0.16f;
			Debug.DrawRay(vector, Vector2.up, Color.yellow);
			Debug.DrawRay(vector2, Vector2.up, Color.yellow);
			Debug.DrawRay(vector3, Vector2.up, Color.yellow);
			RaycastHit2D raycastHit2D = Physics2D.Raycast(vector, Vector2.up, distance, 256);
			RaycastHit2D raycastHit2D2 = Physics2D.Raycast(vector2, Vector2.up, distance, 256);
			RaycastHit2D raycastHit2D3 = Physics2D.Raycast(vector3, Vector2.up, distance, 256);
			return raycastHit2D.collider != null || raycastHit2D2.collider != null || raycastHit2D3.collider != null;
		}
		private List<CollisionSide> CheckTouching(On.HeroController.orig_CheckTouching orig,HeroController self,PhysLayers layer)
		{
			Collider2D col2d=Mirror.GetField<HeroController,Collider2D>(self,"col2d");
			List<CollisionSide> list = new List<CollisionSide>(4);
			Vector3 center = col2d.bounds.center;
			float distance = col2d.bounds.extents.x + 0.16f;
			float distance2 = col2d.bounds.extents.y + 0.16f;
			RaycastHit2D raycastHit2D = Physics2D.Raycast(center, Vector2.down, distance2, 1 << (int)layer);
			RaycastHit2D raycastHit2D2 = Physics2D.Raycast(center, Vector2.right, distance, 1 << (int)layer);
			RaycastHit2D raycastHit2D3 = Physics2D.Raycast(center, Vector2.up, distance2, 1 << (int)layer);
			RaycastHit2D raycastHit2D4 = Physics2D.Raycast(center, Vector2.left, distance, 1 << (int)layer);
			if (raycastHit2D.collider != null)
			{
				list.Add(CollisionSide.top);
			}
			if (raycastHit2D2.collider != null)
			{
				list.Add(CollisionSide.right);
			}
			if (raycastHit2D3.collider != null)
			{
				list.Add(CollisionSide.bottom);
			}
			if (raycastHit2D4.collider != null)
			{
				list.Add(CollisionSide.left);
			}
			return list;
		}
		private List<CollisionSide> CheckTouchingAdvanced(On.HeroController.orig_CheckTouchingAdvanced orig,HeroController self,PhysLayers layer)
		{
			Collider2D col2d=Mirror.GetField<HeroController,Collider2D>(self,"col2d");
			List<CollisionSide> list = new List<CollisionSide>();
			Vector2 origin = new Vector2(col2d.bounds.min.x, col2d.bounds.max.y);
			Vector2 origin2 = new Vector2(col2d.bounds.center.x, col2d.bounds.max.y);
			Vector2 origin3 = new Vector2(col2d.bounds.max.x, col2d.bounds.max.y);
			Vector2 origin4 = new Vector2(col2d.bounds.min.x, col2d.bounds.center.y);
			Vector2 origin5 = new Vector2(col2d.bounds.max.x, col2d.bounds.center.y);
			Vector2 origin6 = new Vector2(col2d.bounds.min.x, col2d.bounds.min.y);
			Vector2 origin7 = new Vector2(col2d.bounds.center.x, col2d.bounds.min.y);
			Vector2 origin8 = new Vector2(col2d.bounds.max.x, col2d.bounds.min.y);
			RaycastHit2D raycastHit2D = Physics2D.Raycast(origin, Vector2.up, 0.16f, 1 << (int)layer);
			RaycastHit2D raycastHit2D2 = Physics2D.Raycast(origin2, Vector2.up, 0.16f, 1 << (int)layer);
			RaycastHit2D raycastHit2D3 = Physics2D.Raycast(origin3, Vector2.up, 0.16f, 1 << (int)layer);
			RaycastHit2D raycastHit2D4 = Physics2D.Raycast(origin3, Vector2.right, 0.16f, 1 << (int)layer);
			RaycastHit2D raycastHit2D5 = Physics2D.Raycast(origin5, Vector2.right, 0.16f, 1 << (int)layer);
			RaycastHit2D raycastHit2D6 = Physics2D.Raycast(origin8, Vector2.right, 0.16f, 1 << (int)layer);
			RaycastHit2D raycastHit2D7 = Physics2D.Raycast(origin8, Vector2.down, 0.16f, 1 << (int)layer);
			RaycastHit2D raycastHit2D8 = Physics2D.Raycast(origin7, Vector2.down, 0.16f, 1 << (int)layer);
			RaycastHit2D raycastHit2D9 = Physics2D.Raycast(origin6, Vector2.down, 0.16f, 1 << (int)layer);
			RaycastHit2D raycastHit2D10 = Physics2D.Raycast(origin6, Vector2.left, 0.16f, 1 << (int)layer);
			RaycastHit2D raycastHit2D11 = Physics2D.Raycast(origin4, Vector2.left, 0.16f, 1 << (int)layer);
			RaycastHit2D raycastHit2D12 = Physics2D.Raycast(origin, Vector2.left, 0.16f, 1 << (int)layer);
			if (raycastHit2D7.collider != null || raycastHit2D8.collider != null || raycastHit2D9.collider != null)
			{
				list.Add(CollisionSide.top);
			}
			if (raycastHit2D4.collider != null || raycastHit2D5.collider != null || raycastHit2D6.collider != null)
			{
				list.Add(CollisionSide.right);
			}
			if (raycastHit2D.collider != null || raycastHit2D2.collider != null || raycastHit2D3.collider != null)
			{
				list.Add(CollisionSide.bottom);
			}
			if (raycastHit2D10.collider != null || raycastHit2D11.collider != null || raycastHit2D12.collider != null)
			{
				list.Add(CollisionSide.left);
			}
			return list;
		}
		private CollisionSide FindCollisionDirection(On.HeroController.orig_FindCollisionDirection orig,HeroController self,Collision2D collision)
		{
			CollisionSide s=orig(self,collision);
			if(s==CollisionSide.top)
				s=CollisionSide.bottom;
			else if(s==CollisionSide.bottom)
				s=CollisionSide.top;
			return s;
		}
		private void OnCollisionEnter2D(On.HeroController.orig_OnCollisionEnter2D orig,HeroController self,Collision2D collision)
		{
			orig(self,collision);
		}
		private void SetupGameRefs(On.HeroController.orig_SetupGameRefs orig,HeroController self)
		{
			orig(self);
		}
		private Vector3 FindGroundPoint(On.HeroController.orig_FindGroundPoint orig,HeroController self,Vector2 startPoint,bool useExtended=false)
		{
			Collider2D col2d=Mirror.GetField<HeroController,Collider2D>(self,"col2d");
			float num = Mirror.GetField<HeroController,float>(self,"FIND_GROUND_POINT_DISTANCE");
			if (useExtended)
			{
				num = Mirror.GetField<HeroController,float>(self,"FIND_GROUND_POINT_DISTANCE_EXT");
			}
			RaycastHit2D raycastHit2D = Physics2D.Raycast(startPoint, Vector2.down, num, 256);
			if (raycastHit2D.collider == null)
			{
				Debug.LogErrorFormat("FindGroundPoint: Could not find ground point below {0}, check reference position is not too high (more than {1} tiles).", new object[]
				{
					startPoint.ToString(),
					num
				});
			}
			return new Vector3(raycastHit2D.point.x, raycastHit2D.point.y - col2d.bounds.extents.y - col2d.offset.y - 0.01f, self.transform.position.z);
		}
		private float FindGroundPointY(On.HeroController.orig_FindGroundPointY orig,HeroController self,float x,float y,bool useExtended=false)
		{
			Collider2D col2d=Mirror.GetField<HeroController,Collider2D>(self,"col2d");
			float num = Mirror.GetField<HeroController,float>(self,"FIND_GROUND_POINT_DISTANCE");
			if (useExtended)
			{
				num = Mirror.GetField<HeroController,float>(self,"FIND_GROUND_POINT_DISTANCE_EXT");
			}
			RaycastHit2D raycastHit2D = Physics2D.Raycast(new Vector2(x, y), Vector2.down, num, 256);
			if (raycastHit2D.collider == null)
			{
				Debug.LogErrorFormat("FindGroundPoint: Could not find ground point below ({0},{1}), check reference position is not too high (more than {2} tiles).", new object[]
				{
					x,
					y,
					num
				});
			}
			return raycastHit2D.point.y - col2d.bounds.extents.y - col2d.offset.y - 0.01f;
		}
		private GatePosition GetGatePosition(On.TransitionPoint.orig_GetGatePosition orig,TransitionPoint self)
		{
			GatePosition g=orig(self);
/*			if(g==GatePosition.top)
				g=GatePosition.bottom;
			else if(g==GatePosition.bottom)
				g=GatePosition.top;
*/			return g;
		}
	}
}