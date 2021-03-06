﻿#region License GNU GPL
// EffectMinMax.cs
// 
// Copyright (C) 2012 - BehaviorIsManaged
// 
// This program is free software; you can redistribute it and/or modify it 
// under the terms of the GNU General Public License as published by the Free Software Foundation;
// either version 2 of the License, or (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; 
// without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. 
// See the GNU General Public License for more details. 
// You should have received a copy of the GNU General Public License along with this program; 
// if not, write to the Free Software Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
#endregion
using System;
using BiM.Protocol.Data;
using BiM.Protocol.Types;

namespace BiM.Behaviors.Game.Effects
{
    [Serializable]
    public class EffectMinMax : EffectBase
    {
        protected short m_maxvalue;
        protected short m_minvalue;

        public EffectMinMax()
        {
            
        }

        public EffectMinMax(EffectMinMax copy)
            : this (copy.Id, copy.ValueMin, copy.ValueMax, copy)
        {
            
        }

        public EffectMinMax(short id, short valuemin, short valuemax, EffectBase effect)
            : base(id, effect)
        {
            m_minvalue = valuemin;
            m_maxvalue = valuemax;
        }

        public EffectMinMax(ObjectEffectMinMax effect)
            : base(effect)
        {
            m_maxvalue = effect.max;
            m_minvalue = effect.min;
        }

        public EffectMinMax(EffectInstanceMinMax effect)
            : base(effect)
        {
            m_maxvalue = (short) effect.max;
            m_minvalue = (short) effect.min;
        }

        public override int ProtocoleId
        {
            get { return 82; }
        }

        public short ValueMin
        {
            get { return m_minvalue; }
            set { m_minvalue = value; }
        }

        public short ValueMax
        {
            get { return m_maxvalue; }
            set { m_maxvalue = value; }
        }

        public override object[] GetValues()
        {
            return new object[] {ValueMin, ValueMax};
        }

        public override ObjectEffect GetObjectEffect()
        {
            return new ObjectEffectMinMax(Id, ValueMin, ValueMax);
        }
        public override EffectInstance GetEffectInstance()
        {
            return new EffectInstanceMinMax()
            {
                effectId = (uint)Id,
                targetId = (int)Targets,
                delay = Delay,
                duration = Duration,
                @group = Group,
                random = Random,
                modificator = Modificator,
                trigger = Trigger,
                hidden = Hidden,
                zoneMinSize = ZoneMinSize,
                zoneSize = ZoneSize,
                zoneShape = (uint) ZoneShape,
                max = (uint) ValueMax,
                min = (uint) ValueMin
            };
        }

        public override bool Equals(object obj)
        {
            if (!(obj is EffectMinMax))
                return false;
            var b = obj as EffectMinMax;
            return base.Equals(obj) && m_minvalue == b.m_minvalue && m_maxvalue == b.m_maxvalue;
        }

        public static bool operator ==(EffectMinMax a, EffectMinMax b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (((object) a == null) || ((object) b == null))
                return false;

            return a.Equals(b);
        }

        public static bool operator !=(EffectMinMax a, EffectMinMax b)
        {
            return !(a == b);
        }

        public bool Equals(EffectMinMax other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return base.Equals(other) && other.m_maxvalue == m_maxvalue && other.m_minvalue == m_minvalue;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int result = base.GetHashCode();
                result = (result*397) ^ m_maxvalue.GetHashCode();
                result = (result*397) ^ m_minvalue.GetHashCode();
                return result;
            }
        }
    }
}