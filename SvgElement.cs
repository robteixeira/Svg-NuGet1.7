using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Xml;
using System.Linq;
using Svg.Transforms;
using System.Reflection;
using System.Threading;
using System.Globalization;

namespace Svg
{
    /// <summary>
    /// The base class of which all SVG elements are derived from.
    /// </summary>
    public abstract class SvgElement : ISvgElement, ISvgTransformable, ICloneable
    {
        //optimization
        protected class PropertyAttributeTuple
        {
            public PropertyDescriptor Property;
            public SvgAttributeAttribute Attribute;
        }

        protected class EventAttributeTuple
        {
            public FieldInfo Event;
            public SvgAttributeAttribute Attribute;
        }

        //reflection cache
        private IEnumerable<PropertyAttributeTuple> _svgPropertyAttributes;
        private IEnumerable<EventAttributeTuple> _svgEventAttributes;

        internal SvgElement _parent;
        private string _elementName;
        private SvgAttributeCollection _attributes;
        private EventHandlerList _eventHandlers;
        private SvgElementCollection _children;
        private static readonly object _loadEventKey = new object();
        private Matrix _graphicsMatrix;
        private SvgCustomAttributeCollection _customAttributes;

        /// <summary>
        /// Gets the name of the element.
        /// </summary>
        protected internal string ElementName
        {
            get
            {
                if (string.IsNullOrEmpty(this._elementName))
                {
                    var attr = TypeDescriptor.GetAttributes(this).OfType<SvgElementAttribute>().SingleOrDefault();

                    if (attr != null)
                    {
                        this._elementName = attr.ElementName;
                    }
                }

                return this._elementName;
            }
            internal set { this._elementName = value; }
        }

        /// <summary>
        /// Gets or sets the content of the element.
        /// </summary>
        private string _content;
        public virtual string Content
        {
            get
            {
            	return _content;
            }
            set
            {
            	if(_content != null)
            	{
            		var oldVal = _content;
            		_content = value;
            		if(_content != oldVal)
            			OnContentChanged(new ContentEventArgs{ Content = value });
            	}
            	else
            	{
            		_content = value;
            		OnContentChanged(new ContentEventArgs{ Content = value });
            	}
            }
        }

        /// <summary>
        /// Gets an <see cref="EventHandlerList"/> of all events belonging to the element.
        /// </summary>
        protected virtual EventHandlerList Events
        {
            get { return this._eventHandlers; }
        }

        /// <summary>
        /// Occurs when the element is loaded.
        /// </summary>
        public event EventHandler Load
        {
            add { this.Events.AddHandler(_loadEventKey, value); }
            remove { this.Events.RemoveHandler(_loadEventKey, value); }
        }

        /// <summary>
        /// Gets a collection of all child <see cref="SvgElements"/>.
        /// </summary>
        public virtual SvgElementCollection Children
        {
            get { return this._children; }
        }

        /// <summary>
        /// Gets a value to determine whether the element has children.
        /// </summary>
        public virtual bool HasChildren()
        {
            return (this.Children.Count > 0);
        }

        /// <summary>
        /// Gets the parent <see cref="SvgElement"/>.
        /// </summary>
        /// <value>An <see cref="SvgElement"/> if one exists; otherwise null.</value>
        public virtual SvgElement Parent
        {
            get { return this._parent; }
        }

        /// <summary>
        /// Gets the owner <see cref="SvgDocument"/>.
        /// </summary>
        public virtual SvgDocument OwnerDocument
        {
        	get
        	{
        		if (this is SvgDocument)
        		{
        			return this as SvgDocument;
        		}
        		else
        		{
        			if(this.Parent != null)
        				return Parent.OwnerDocument;
        			else
        				return null;
        		}
        	}
        }

        /// <summary>
        /// Gets a collection of element attributes.
        /// </summary>
        protected internal virtual SvgAttributeCollection Attributes
        {
            get
            {
                if (this._attributes == null)
                {
                    this._attributes = new SvgAttributeCollection(this);
                }

                return this._attributes;
            }
        }

        /// <summary>
        /// Gets a collection of custom attributes
        /// </summary>
        public SvgCustomAttributeCollection CustomAttributes
        {
            get { return this._customAttributes; }
        }

        /// <summary>
        /// Applies the required transforms to <see cref="SvgRenderer"/>.
        /// </summary>
        /// <param name="renderer">The <see cref="SvgRenderer"/> to be transformed.</param>
        protected internal virtual void PushTransforms(SvgRenderer renderer)
        {
            _graphicsMatrix = renderer.Transform;

            // Return if there are no transforms
            if (this.Transforms == null || this.Transforms.Count == 0)
            {
                return;
            }

            Matrix transformMatrix = renderer.Transform;

            foreach (SvgTransform transformation in this.Transforms)
            {
                transformMatrix.Multiply(transformation.Matrix);
            }

            renderer.Transform = transformMatrix;
        }

        /// <summary>
        /// Removes any previously applied transforms from the specified <see cref="SvgRenderer"/>.
        /// </summary>
        /// <param name="renderer">The <see cref="SvgRenderer"/> that should have transforms removed.</param>
        protected internal virtual void PopTransforms(SvgRenderer renderer)
        {
            renderer.Transform = _graphicsMatrix;
            _graphicsMatrix = null;
        }

        /// <summary>
        /// Applies the required transforms to <see cref="SvgRenderer"/>.
        /// </summary>
        /// <param name="renderer">The <see cref="SvgRenderer"/> to be transformed.</param>
        void ISvgTransformable.PushTransforms(SvgRenderer renderer)
        {
            this.PushTransforms(renderer);
        }

        /// <summary>
        /// Removes any previously applied transforms from the specified <see cref="SvgRenderer"/>.
        /// </summary>
        /// <param name="renderer">The <see cref="SvgRenderer"/> that should have transforms removed.</param>
        void ISvgTransformable.PopTransforms(SvgRenderer renderer)
        {
            this.PopTransforms(renderer);
        }

        /// <summary>
        /// Gets or sets the element transforms.
        /// </summary>
        /// <value>The transforms.</value>
        [SvgAttribute("transform")]
        public SvgTransformCollection Transforms
        {
            get { return (this.Attributes.GetAttribute<SvgTransformCollection>("transform")); }
            set 
            { 
            	var old = this.Transforms;
            	if(old != null)
            		old.TransformChanged -= Attributes_AttributeChanged;
            	value.TransformChanged += Attributes_AttributeChanged;
            	this.Attributes["transform"] = value; 
            }
        }

        /// <summary>
        /// Gets or sets the ID of the element.
        /// </summary>
        /// <exception cref="SvgException">The ID is already used within the <see cref="SvgDocument"/>.</exception>
        [SvgAttribute("id")]
        public string ID
        {
            get { return this.Attributes.GetAttribute<string>("id"); }
            set
            {
                SetAndForceUniqueID(value, false);
            }
        }

        public void SetAndForceUniqueID(string value, bool autoForceUniqueID = true, Action<SvgElement, string, string> logElementOldIDNewID = null)
        {
            // Don't do anything if it hasn't changed
            if (string.Compare(this.ID, value) == 0)
            {
                return;
            }

            if (this.OwnerDocument != null)
            {
                this.OwnerDocument.IdManager.Remove(this);
            }

            this.Attributes["id"] = value;

            if (this.OwnerDocument != null)
            {
                this.OwnerDocument.IdManager.AddAndForceUniqueID(this, null, autoForceUniqueID, logElementOldIDNewID);
            }
        }

        /// <summary>
        /// Only used by the ID Manager
        /// </summary>
        /// <param name="newID"></param>
        internal void ForceUniqueID(string newID)
        {
            this.Attributes["id"] = newID;
        }

        /// <summary>
        /// Called by the underlying <see cref="SvgElement"/> when an element has been added to the
        /// <see cref="Children"/> collection.
        /// </summary>
        /// <param name="child">The <see cref="SvgElement"/> that has been added.</param>
        /// <param name="index">An <see cref="int"/> representing the index where the element was added to the collection.</param>
        protected virtual void AddElement(SvgElement child, int index)
        {
        }
        
        /// <summary>
        /// Fired when an Element was added to the children of this Element
        /// </summary>
		public event EventHandler<ChildAddedEventArgs> ChildAdded;

        /// <summary>
        /// Calls the <see cref="AddElement"/> method with the specified parameters.
        /// </summary>
        /// <param name="child">The <see cref="SvgElement"/> that has been added.</param>
        /// <param name="index">An <see cref="int"/> representing the index where the element was added to the collection.</param>
        internal void OnElementAdded(SvgElement child, int index)
        {
            this.AddElement(child, index);
            SvgElement sibling = null;
            if(index < (Children.Count - 1))
            {
            	sibling = Children[index + 1];
            }
            var handler = ChildAdded;
            if(handler != null)
            {
            	handler(this, new ChildAddedEventArgs { NewChild = child, BeforeSibling = sibling });
            }
        }

        /// <summary>
        /// Called by the underlying <see cref="SvgElement"/> when an element has been removed from the
        /// <see cref="Children"/> collection.
        /// </summary>
        /// <param name="child">The <see cref="SvgElement"/> that has been removed.</param>
        protected virtual void RemoveElement(SvgElement child)
        {
        }

        /// <summary>
        /// Calls the <see cref="RemoveElement"/> method with the specified <see cref="SvgElement"/> as the parameter.
        /// </summary>
        /// <param name="child">The <see cref="SvgElement"/> that has been removed.</param>
        internal void OnElementRemoved(SvgElement child)
        {
            this.RemoveElement(child);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SvgElement"/> class.
        /// </summary>
        public SvgElement()
        {
            this._children = new SvgElementCollection(this);
            this._eventHandlers = new EventHandlerList();
            this._elementName = string.Empty;
            this._customAttributes = new SvgCustomAttributeCollection(this);
            
            Transforms = new SvgTransformCollection();
            
            //subscribe to attribute events
            Attributes.AttributeChanged += Attributes_AttributeChanged;
            CustomAttributes.AttributeChanged += Attributes_AttributeChanged;

            //find svg attribute descriptions
            _svgPropertyAttributes = from PropertyDescriptor a in TypeDescriptor.GetProperties(this)
                            let attribute = a.Attributes[typeof(SvgAttributeAttribute)] as SvgAttributeAttribute
                            where attribute != null
                            select new PropertyAttributeTuple { Property = a, Attribute = attribute };

            _svgEventAttributes = from EventDescriptor a in TypeDescriptor.GetEvents(this)
                            let attribute = a.Attributes[typeof(SvgAttributeAttribute)] as SvgAttributeAttribute
                            where attribute != null
                            select new EventAttributeTuple { Event = a.ComponentType.GetField(a.Name, BindingFlags.Instance | BindingFlags.NonPublic), Attribute = attribute };

        }

        //dispatch attribute event
        void Attributes_AttributeChanged(object sender, AttributeEventArgs e)
        {
        	OnAttributeChanged(e);
        }

		public virtual void InitialiseFromXML(XmlTextReader reader, SvgDocument document)
		{
            throw new NotImplementedException();
		}


        /// <summary>
        /// Renders this element to the <see cref="SvgRenderer"/>.
        /// </summary>
        /// <param name="renderer">The <see cref="SvgRenderer"/> that the element should use to render itself.</param>
        public void RenderElement(SvgRenderer renderer)
        {
            this.Render(renderer);
        }

        public void WriteElement(XmlTextWriter writer)
        {
            //Save previous culture and switch to invariant for writing
            var previousCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            this.Write(writer);

            //Switch culture back
            Thread.CurrentThread.CurrentCulture = previousCulture;
        }

        protected virtual void WriteStartElement(XmlTextWriter writer)
        {
            if (this.ElementName != String.Empty)
            {
                writer.WriteStartElement(this.ElementName);
                if (this.ElementName == "svg")
                {
					foreach (var ns in SvgAttributeAttribute.Namespaces)
					{
						if (string.IsNullOrEmpty(ns.Key))
							writer.WriteAttributeString("xmlns", ns.Value);
						else
							writer.WriteAttributeString("xmlns:" + ns.Key, ns.Value);
					}
					writer.WriteAttributeString("version", "1.1");
				}
            }
            this.WriteAttributes(writer);
        }

        protected virtual void WriteEndElement(XmlTextWriter writer)
        {
            if (this.ElementName != String.Empty)
            {
                writer.WriteEndElement();
            }
        }

        protected virtual void WriteAttributes(XmlTextWriter writer)
        {
            //properties
            foreach (var attr in _svgPropertyAttributes)
            {
                if (attr.Property.Converter.CanConvertTo(typeof(string)))
                {
                    object propertyValue = attr.Property.GetValue(this);

                    var forceWrite = false;
                    if ((attr.Attribute.Name == "fill") && (Parent != null))
                    {
                    	if(propertyValue == SvgColourServer.NotSet) continue;
                    	
                        object parentValue;
                        if (TryResolveParentAttributeValue(attr.Attribute.Name, out parentValue))
                        {
                            if ((parentValue == propertyValue) 
                                || ((parentValue != null) &&  parentValue.Equals(propertyValue)))
                                continue;
                            
                            forceWrite = true;
                        }
                    }

                    if (propertyValue != null)
                    {
                        var type = propertyValue.GetType();
                        string value = (string)attr.Property.Converter.ConvertTo(propertyValue, typeof(string));

                        if (!SvgDefaults.IsDefault(attr.Attribute.Name, value) || forceWrite)
                        {
                            writer.WriteAttributeString(attr.Attribute.NamespaceAndName, value);
                        }
                    }
                    else if(attr.Attribute.Name == "fill") //if fill equals null, write 'none'
                    {
                        string value = (string)attr.Property.Converter.ConvertTo(propertyValue, typeof(string));
                        writer.WriteAttributeString(attr.Attribute.NamespaceAndName, value);
                    }
                }
            }

            
            //events
            if(AutoPublishEvents)
            {
	            foreach (var attr in _svgEventAttributes)
	            {
	                var evt = attr.Event.GetValue(this);
	                
	                //if someone has registered publish the attribute
	                if (evt != null && !string.IsNullOrEmpty(this.ID))
	                {
	                    writer.WriteAttributeString(attr.Attribute.Name, this.ID + "/" + attr.Attribute.Name);
	                }
	            }
            }

            //add the custom attributes
            foreach (var item in this._customAttributes)
            {
                writer.WriteAttributeString(item.Key, item.Value);
            }
        }
        
        public bool AutoPublishEvents = true;

        private bool TryResolveParentAttributeValue(string attributeKey, out object parentAttributeValue)
        {
            parentAttributeValue = null;

            //attributeKey = char.ToUpper(attributeKey[0]) + attributeKey.Substring(1);

            var currentParent = Parent;
            var resolved = false;
            while (currentParent != null)
            {
                if (currentParent.Attributes.ContainsKey(attributeKey))
                {
                    resolved = true;
                    parentAttributeValue = currentParent.Attributes[attributeKey];
                    if (parentAttributeValue != null)
                        break;
                }
                currentParent = currentParent.Parent;
            }

            return resolved;
        }

        protected virtual void Write(XmlTextWriter writer)
        {
            if (this.ElementName != String.Empty)
            {
                this.WriteStartElement(writer);
                this.WriteChildren(writer);
                this.WriteEndElement(writer);
            }
        }

        protected virtual void WriteChildren(XmlTextWriter writer)
        {
            //write the content
            if(!String.IsNullOrEmpty(this.Content))
                writer.WriteString(this.Content);

            //write all children
            foreach (SvgElement child in this.Children)
            {
                child.Write(writer);
            }
        }

        /// <summary>
        /// Renders the <see cref="SvgElement"/> and contents to the specified <see cref="SvgRenderer"/> object.
        /// </summary>
        /// <param name="renderer">The <see cref="SvgRenderer"/> object to render to.</param>
        protected virtual void Render(SvgRenderer renderer)
        {
            this.PushTransforms(renderer);
            this.RenderChildren(renderer);
            this.PopTransforms(renderer);
        }

        /// <summary>
        /// Renders the children of this <see cref="SvgElement"/>.
        /// </summary>
        /// <param name="renderer">The <see cref="SvgRenderer"/> to render the child <see cref="SvgElement"/>s to.</param>
        protected virtual void RenderChildren(SvgRenderer renderer)
        {
            foreach (SvgElement element in this.Children)
            {
                element.Render(renderer);
            }
        }

        /// <summary>
        /// Renders the <see cref="SvgElement"/> and contents to the specified <see cref="SvgRenderer"/> object.
        /// </summary>
        /// <param name="renderer">The <see cref="SvgRenderer"/> object to render to.</param>
        void ISvgElement.Render(SvgRenderer renderer)
        {
            this.Render(renderer);
        }
        
        /// <summary>
        /// Recursive method to add up the paths of all children
        /// </summary>
        /// <param name="elem"></param>
        /// <param name="path"></param>
        protected void AddPaths(SvgElement elem, GraphicsPath path)
        {
        	foreach(var child in elem.Children)
        	{
        		if (child is SvgVisualElement)
        		{
        			if(!(child is SvgGroup))
        			{
        				var childPath = ((SvgVisualElement)child).Path;
        				
        				if (childPath != null)
        				{
        					childPath = (GraphicsPath)childPath.Clone();
        					if(child.Transforms != null)
        						childPath.Transform(child.Transforms.GetMatrix());
        					
        					path.AddPath(childPath, false);
        				}
        			}
        		}
        			
        		AddPaths(child, path);
        	}
        }
        
        /// <summary>
        /// Recursive method to add up the paths of all children
        /// </summary>
        /// <param name="elem"></param>
        /// <param name="path"></param>
        protected GraphicsPath GetPaths(SvgElement elem)
        {
        	var ret = new GraphicsPath();
        	
        	foreach(var child in elem.Children)
        	{
        		if (child is SvgVisualElement)
        		{
        			if(!(child is SvgGroup))
        			{
        				var childPath = ((SvgVisualElement)child).Path;
        				
        				if (childPath != null)
        				{
        					childPath = (GraphicsPath)childPath.Clone();
        					if(child.Transforms != null)
        						childPath.Transform(child.Transforms.GetMatrix());
        					
        					ret.AddPath(childPath, false);
        				}
        			}
        			else
        			{
        				var childPath = GetPaths(child);
        				if(child.Transforms != null)
        					childPath.Transform(child.Transforms.GetMatrix());
        			}
        		}
        			
        	}
        	
        	return ret;
        }

        /// <summary>
        /// Creates a new object that is a copy of the current instance.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public virtual object Clone()
        {
            return this.MemberwiseClone();
        }

    	public abstract SvgElement DeepCopy();

		public virtual SvgElement DeepCopy<T>() where T : SvgElement, new()
		{
			var newObj = new T();
			newObj.ID = this.ID;
			newObj.Content = this.Content;
			newObj.ElementName = this.ElementName;
			
//			if (this.Parent != null)
	//			this.Parent.Children.Add(newObj);

			if (this.Transforms != null)
			{
				newObj.Transforms = this.Transforms.Clone() as SvgTransformCollection;
			}

			foreach (var child in this.Children)
			{
				newObj.Children.Add(child.DeepCopy());
			}
			
			foreach (var attr in this._svgEventAttributes)
			{
				var evt = attr.Event.GetValue(this);
				
				//if someone has registered also register here
				if (evt != null)
				{
					if(attr.Event.Name == "MouseDown")
						newObj.MouseDown += delegate {  };
					else if (attr.Event.Name == "MouseUp")
						newObj.MouseUp += delegate {  };
					else if (attr.Event.Name == "MouseOver")
						newObj.MouseOver += delegate {  };
					else if (attr.Event.Name == "MouseOut")
						newObj.MouseOut += delegate {  };
					else if (attr.Event.Name == "MouseMove")
						newObj.MouseMove += delegate {  };
					else if (attr.Event.Name == "MouseScroll")
						newObj.MouseScroll += delegate {  };
					else if (attr.Event.Name == "Click")
						newObj.Click += delegate {  };
					else if (attr.Event.Name == "Change") //text element
						(newObj as SvgText).Change += delegate {  };
				}
			}
			
			if(this._customAttributes.Count > 0)
			{
				foreach (var element in _customAttributes) 
				{
					newObj.CustomAttributes.Add(element.Key, element.Value);
				}
			}
				
			return newObj;
        }
		
		/// <summary>
        /// Fired when an Atrribute of this Element has changed
        /// </summary>
		public event EventHandler<AttributeEventArgs> AttributeChanged;
		
		protected void OnAttributeChanged(AttributeEventArgs args)
		{
			var handler = AttributeChanged;
			if(handler != null)
			{
				handler(this, args);
			}
		}
		
		/// <summary>
        /// Fired when an Atrribute of this Element has changed
        /// </summary>
		public event EventHandler<ContentEventArgs> ContentChanged;
		
		protected void OnContentChanged(ContentEventArgs args)
		{
			var handler = ContentChanged;
			if(handler != null)
			{
				handler(this, args);
			}
		}

        #region graphical EVENTS

        /*  
            	onfocusin = "<anything>"
            	onfocusout = "<anything>"
            	onactivate = "<anything>"
                onclick = "<anything>"
                onmousedown = "<anything>"
                onmouseup = "<anything>"
                onmouseover = "<anything>"
                onmousemove = "<anything>"
            	onmouseout = "<anything>" 
         */

        /// <summary>
        /// Use this method to provide your implementation ISvgEventCaller which can register Actions 
        /// and call them if one of the events occurs. Make sure, that your SvgElement has a unique ID.
        /// The SvgTextElement overwrites this and regsiters the Change event tor its text content.
        /// </summary>
        /// <param name="caller"></param>
        public virtual void RegisterEvents(ISvgEventCaller caller)
        {
            if (caller != null && !string.IsNullOrEmpty(this.ID))
            {
                var rpcID = this.ID + "/";

                caller.RegisterAction<float, float, int, int, bool, bool, bool, string>(rpcID + "onclick", CreateMouseEventAction(RaiseMouseClick));
                caller.RegisterAction<float, float, int, int, bool, bool, bool, string>(rpcID + "onmousedown", CreateMouseEventAction(RaiseMouseDown));
                caller.RegisterAction<float, float, int, int, bool, bool, bool, string>(rpcID + "onmouseup", CreateMouseEventAction(RaiseMouseUp));
                caller.RegisterAction<float, float, int, int, bool, bool, bool, string>(rpcID + "onmousemove", CreateMouseEventAction(RaiseMouseMove));
                caller.RegisterAction<float, float, int, int, bool, bool, bool, string>(rpcID + "onmouseover", CreateMouseEventAction(RaiseMouseOver));
                caller.RegisterAction<float, float, int, int, bool, bool, bool, string>(rpcID + "onmouseout", CreateMouseEventAction(RaiseMouseOut));
                caller.RegisterAction<int, bool, bool, bool, string>(rpcID + "onmousescroll", OnMouseScroll);
            }
        }
        
        /// <summary>
        /// Use this method to provide your implementation ISvgEventCaller to unregister Actions
        /// </summary>
        /// <param name="caller"></param>
        public virtual void UnregisterEvents(ISvgEventCaller caller)
        {
        	if (caller != null && !string.IsNullOrEmpty(this.ID))
        	{
        		var rpcID = this.ID + "/";

        		caller.UnregisterAction(rpcID + "onclick");
        		caller.UnregisterAction(rpcID + "onmousedown");
        		caller.UnregisterAction(rpcID + "onmouseup");
        		caller.UnregisterAction(rpcID + "onmousemove");
        		caller.UnregisterAction(rpcID + "onmousescroll");
        		caller.UnregisterAction(rpcID + "onmouseover");
        		caller.UnregisterAction(rpcID + "onmouseout");
        	}
        }

        [SvgAttribute("onclick")]
        public event EventHandler<MouseArg> Click;

        [SvgAttribute("onmousedown")]
        public event EventHandler<MouseArg> MouseDown;

        [SvgAttribute("onmouseup")]
        public event EventHandler<MouseArg> MouseUp;
        
        [SvgAttribute("onmousemove")]
        public event EventHandler<MouseArg> MouseMove;

        [SvgAttribute("onmousescroll")]
        public event EventHandler<MouseScrollArg> MouseScroll;
        
        [SvgAttribute("onmouseover")]
        public event EventHandler<MouseArg> MouseOver;

        [SvgAttribute("onmouseout")]
        public event EventHandler<MouseArg> MouseOut;
        
        protected Action<float, float, int, int, bool, bool, bool, string> CreateMouseEventAction(Action<object, MouseArg> eventRaiser)
        {
        	return (x, y, button, clickCount, altKey, shiftKey, ctrlKey, sessionID) =>
        		eventRaiser(this, new MouseArg { x = x, y = y, Button = button, ClickCount = clickCount, AltKey = altKey, ShiftKey = shiftKey, CtrlKey = ctrlKey, SessionID = sessionID });
        }
        
        //click
        protected void RaiseMouseClick(object sender, MouseArg e)
        {
        	var handler = Click;
        	if (handler != null)
        	{
        		handler(sender, e);
            }
        }

        //down
        protected void RaiseMouseDown(object sender, MouseArg e)
        {
        	var handler = MouseDown;
            if (handler != null)
            {
                handler(sender, e);
            }
        }

        //up
        protected void RaiseMouseUp(object sender, MouseArg e)
        {
        	var handler = MouseUp;
            if (handler != null)
            {
                handler(sender, e);
            }
        }

        protected void RaiseMouseMove(object sender, MouseArg e)
        {
        	var handler = MouseMove;
            if (handler != null)
            {
                handler(sender, e);
            }
        }
        
        //over
        protected void RaiseMouseOver(object sender, MouseArg args)
        {
        	var handler = MouseOver;
            if (handler != null)
            {
                handler(sender, args);
            }
        }

        //out
        protected void RaiseMouseOut(object sender, MouseArg args)
        {
        	var handler = MouseOut;
            if (handler != null)
            {
                handler(sender, args);
            }
        }
        
        
        //scroll
        protected void OnMouseScroll(int scroll, bool ctrlKey, bool shiftKey, bool altKey, string sessionID)
        {
        	RaiseMouseScroll(this, new MouseScrollArg { Scroll = scroll, AltKey = altKey, ShiftKey = shiftKey, CtrlKey = ctrlKey, SessionID = sessionID});
        }
        
        protected void RaiseMouseScroll(object sender, MouseScrollArg e)
        {
        	var handler = MouseScroll;
            if (handler != null)
            {
                handler(sender, e);
            }
        }
        
        #endregion graphical EVENTS

    }
    
    public class SVGArg : EventArgs
    {
    	public string SessionID;
    }
    	
    
    /// <summary>
    /// Describes the Attribute which was set
    /// </summary>
    public class AttributeEventArgs : SVGArg
    {
    	public string Attribute;
    	public object Value;
    }
    
    /// <summary>
    /// Content of this whas was set
    /// </summary>
    public class ContentEventArgs : SVGArg
    {
    	public string Content;
    }
    
    /// <summary>
    /// Describes the Attribute which was set
    /// </summary>
    public class ChildAddedEventArgs : SVGArg
    {
    	public SvgElement NewChild;
    	public SvgElement BeforeSibling;
    }

    //deriving class registers event actions and calls the actions if the event occurs
    public interface ISvgEventCaller
    {
        void RegisterAction(string rpcID, Action action);
        void RegisterAction<T1>(string rpcID, Action<T1> action);
        void RegisterAction<T1, T2>(string rpcID, Action<T1, T2> action);
        void RegisterAction<T1, T2, T3>(string rpcID, Action<T1, T2, T3> action);
        void RegisterAction<T1, T2, T3, T4>(string rpcID, Action<T1, T2, T3, T4> action);
        void RegisterAction<T1, T2, T3, T4, T5>(string rpcID, Action<T1, T2, T3, T4, T5> action);
        void RegisterAction<T1, T2, T3, T4, T5, T6>(string rpcID, Action<T1, T2, T3, T4, T5, T6> action);
        void RegisterAction<T1, T2, T3, T4, T5, T6, T7>(string rpcID, Action<T1, T2, T3, T4, T5, T6, T7> action);
        void RegisterAction<T1, T2, T3, T4, T5, T6, T7, T8>(string rpcID, Action<T1, T2, T3, T4, T5, T6, T7, T8> action);
        void UnregisterAction(string rpcID);
    }

    /// <summary>
    /// Represents the state of the mouse at the moment the event occured.
    /// </summary>
    public class MouseArg : SVGArg
    {
        public float x;
        public float y;

        /// <summary>
        /// 1 = left, 2 = middle, 3 = right
        /// </summary>
        public int Button;
        
        /// <summary>
        /// Amount of mouse clicks, e.g. 2 for double click
        /// </summary>
        public int ClickCount = -1;
        
        /// <summary>
        /// Alt modifier key pressed
        /// </summary>
        public bool AltKey;
        
        /// <summary>
        /// Shift modifier key pressed
        /// </summary>
        public bool ShiftKey;
        
        /// <summary>
        /// Control modifier key pressed
        /// </summary>
        public bool CtrlKey;
    }
    
    /// <summary>
    /// Represents a string argument
    /// </summary>
    public class StringArg : SVGArg
    {
        public string s;
    }
    
    public class MouseScrollArg : SVGArg
    {
    	public int Scroll;
    	
    	/// <summary>
        /// Alt modifier key pressed
        /// </summary>
        public bool AltKey;
        
        /// <summary>
        /// Shift modifier key pressed
        /// </summary>
        public bool ShiftKey;
        
        /// <summary>
        /// Control modifier key pressed
        /// </summary>
        public bool CtrlKey;
    }

    internal interface ISvgElement
    {
		SvgElement Parent {get;}
		SvgElementCollection Children { get; }

        void Render(SvgRenderer renderer);
    }
}