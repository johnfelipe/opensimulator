/*
* Copyright (c) OpenSim project, http://sim.opensecondlife.org/
*
* Redistribution and use in source and binary forms, with or without
* modification, are permitted provided that the following conditions are met:
*     * Redistributions of source code must retain the above copyright
*       notice, this list of conditions and the following disclaimer.
*     * Redistributions in binary form must reproduce the above copyright
*       notice, this list of conditions and the following disclaimer in the
*       documentation and/or other materials provided with the distribution.
*     * Neither the name of the <organization> nor the
*       names of its contributors may be used to endorse or promote products
*       derived from this software without specific prior written permission.
*
* THIS SOFTWARE IS PROVIDED BY <copyright holder> ``AS IS'' AND ANY
* EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
* WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
* DISCLAIMED. IN NO EVENT SHALL <copyright holder> BE LIABLE FOR ANY
* DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
* (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
* LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
* ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
* (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
* SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
* 
*/
using System;
using System.Collections.Generic;
using PhysicsSystem;

namespace PhysXplugin
{
	/// <summary>
	/// Will be the PhysX plugin but for now will be a very basic physics engine
	/// </summary>
	public class PhysXPlugin : IPhysicsPlugin
	{
		private PhysXScene _mScene;
		
		public PhysXPlugin()
		{
			
		}
		
		public bool Init()
		{
			return true;
		}
		
		public PhysicsScene GetScene()
		{
			if(_mScene == null)
			{
				_mScene = new PhysXScene();
			}
			return(_mScene);
		}
		
		public string GetName()
		{
			return("PhysX");
		}
		
		public void Dispose()
		{
			
		}
	}
	
	public class PhysXScene :PhysicsScene
	{
		private List<PhysXActor> _actors = new List<PhysXActor>();
		private float[] _heightMap;
		
		public PhysXScene()
		{
			
		}
		
		public override PhysicsActor AddAvatar(PhysicsVector position)
		{
			PhysXActor act = new PhysXActor();
			act.Position = position;
			_actors.Add(act);
			return act;
		}
		
		public override void Simulate(float timeStep)
		{
			foreach (PhysXActor actor in _actors)
			{
				actor.Position.X = actor.Position.X + actor.Velocity.X * timeStep;
				actor.Position.Y = actor.Position.Y + actor.Velocity.Y * timeStep;
				actor.Position.Z = actor.Position.Z + actor.Velocity.Z * timeStep;
				actor.Position.Z = _heightMap[(int)actor.Position.Y * 256 + (int)actor.Position.X]+1;
				if(actor.Position.X<0)
				{
					actor.Position.X = 0;
					actor.Velocity.X = 0;
				}
				if(actor.Position.Y < 0)
				{
					actor.Position.Y = 0;
					actor.Velocity.Y = 0;
				}
				if(actor.Position.X > 255)
				{
					actor.Position.X = 255;
					actor.Velocity.X = 0;
				}
				if(actor.Position.Y > 255) 
				{
					actor.Position.Y = 255;
					actor.Velocity.X = 0;
				}
			}
		}
		
		public override void GetResults()
		{
		
		}
		
		public override bool IsThreaded
		{
			get
			{
				return(false); // for now we won't be multithreaded
			}
		}
		
		public override void SetTerrain(float[] heightMap)
		{
			this._heightMap = heightMap;
		}
	}
	
	public  class PhysXActor : PhysicsActor
	{
		private PhysicsVector _position;
		private PhysicsVector _velocity;
		private PhysicsVector _acceleration;
		
		public PhysXActor()
		{
			_velocity = new PhysicsVector();
			_position = new PhysicsVector();
			_acceleration = new PhysicsVector();
		}
		
		public override PhysicsVector Position
		{
			get
			{
				return _position;
			}
			set
			{
				_position = value;
			}
		}
		
		public override PhysicsVector Velocity
		{
			get
			{
				return _velocity;
			}
			set
			{
				_velocity = value;
			}
		}
		
		public override PhysicsVector Acceleration
		{
			get
			{
				return _acceleration;
			}
			
		}
		public void SetAcceleration (PhysicsVector accel)
		{
			this._acceleration = accel;
		}
		
		public override void AddForce(PhysicsVector force)
		{
			
		}
		
		public override void SetMomentum(PhysicsVector momentum)
		{
			
		}
	}

}
