// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.PowerToys.Settings.UI.Library
{
    public class AdvancedPasteShortcut : INotifyPropertyChanged
    {
        public AdvancedPasteShortcut(int id)
        {
            Id = id;
            Name = string.Empty;
            Prompt = string.Empty;
            Model = string.Empty;
        }

        public AdvancedPasteShortcut()
        {
            Id = 0;
            Name = string.Empty;
            Prompt = string.Empty;
            Model = string.Empty;
        }

        public AdvancedPasteShortcut(int id, string name, string prompt, string model)
        {
            Id = id;
            Name = name;
            Prompt = prompt;
            Model = model;
        }

        private int _id;
        private string _name;
        private string _prompt;
        private string _model;

        public int Id
        {
            get
            {
                return _id;
            }

            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged();
                }
            }
        }

        [JsonPropertyName("name")]
        public string Name
        {
            get
            {
                return _name;
            }

            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        [JsonPropertyName("prompt")]
        public string Prompt
        {
            get
            {
                return _prompt;
            }

            set
            {
                if (_prompt != value)
                {
                    _prompt = value;
                    OnPropertyChanged();
                }
            }
        }

        [JsonPropertyName("model")]
        public string Model
        {
            get
            {
                return _model;
            }

            set
            {
                if (_model != value)
                {
                    _model = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public void Update(AdvancedPasteShortcut modifiedShortcut)
        {
            ArgumentNullException.ThrowIfNull(modifiedShortcut);

            Id = modifiedShortcut.Id;
            Name = modifiedShortcut.Name;
            Model = modifiedShortcut.Model;
            Prompt = modifiedShortcut.Prompt;
        }

        public string ToJsonString()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}
