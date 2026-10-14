// <event-map latitude="38.7" longitude="-9.2"> shows a Leaflet map with a marker.
// With latitude-input and longitude-input (ids of number inputs) it becomes a picker: clicking the
// map moves the marker and fills the inputs, and typing in the inputs moves the marker.
// Give each map a unique data-permanent value, so Blazor's page updates leave Leaflet's content alone.
class EventMap extends HTMLElement {
    connectedCallback() {
        if (this.map) return;

        const latitudeInput = document.getElementById(this.getAttribute("latitude-input"));
        const longitudeInput = document.getElementById(this.getAttribute("longitude-input"));
        const isPicker = latitudeInput && longitudeInput;
        const start = isPicker
            ? toLatLng(latitudeInput.value, longitudeInput.value)
            : toLatLng(this.getAttribute("latitude"), this.getAttribute("longitude"));

        this.map = L.map(this, { scrollWheelZoom: false });
        L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
            maxZoom: 19,
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
        }).addTo(this.map);

        let marker = null;
        const placeMarker = latLng => {
            if (marker) marker.setLatLng(latLng);
            else marker = L.marker(latLng).addTo(this.map);
        };

        if (start) {
            this.map.setView(start, 15);
            placeMarker(start);
        } else {
            this.map.setView([20, 0], 2);
        }

        if (!isPicker) return;

        this.map.on("click", e => {
            const latLng = e.latlng.wrap();
            placeMarker(latLng);
            // About 10 cm precision, plenty for a venue.
            latitudeInput.value = latLng.lat.toFixed(6);
            longitudeInput.value = latLng.lng.toFixed(6);
        });

        const onInput = () => {
            const latLng = toLatLng(latitudeInput.value, longitudeInput.value);
            if (latLng) {
                placeMarker(latLng);
                this.map.panTo(latLng);
            }
        };
        // Aborted when the element is removed, which removes these listeners.
        this.listeners = new AbortController();
        latitudeInput.addEventListener("input", onInput, { signal: this.listeners.signal });
        longitudeInput.addEventListener("input", onInput, { signal: this.listeners.signal });
    }

    disconnectedCallback() {
        this.listeners?.abort();
        this.map?.remove();
        this.map = null;
    }
}

function toLatLng(latitude, longitude) {
    // Number("") is 0, so blank values are checked separately.
    if (!latitude || !longitude) return null;
    const lat = Number(latitude);
    const lng = Number(longitude);
    const valid = lat >= -90 && lat <= 90 && lng >= -180 && lng <= 180;
    return valid ? [lat, lng] : null;
}

customElements.define("event-map", EventMap);
