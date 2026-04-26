import { Injectable, signal, computed } from '@angular/core';

export type GeoState =
  | { status: 'idle' }
  | { status: 'requesting' }
  | { status: 'granted'; lat: number; lng: number }
  | { status: 'denied' }
  | { status: 'unavailable' };

@Injectable({ providedIn: 'root' })
export class LocationFilterService {
  readonly usOnly = signal(true);
  readonly geoState = signal<GeoState>({ status: 'idle' });
  readonly radiusMiles = signal(25);
  readonly hasCoords = computed(() => this.geoState().status === 'granted');

  toggle(): void {
    this.usOnly.update(v => !v);
  }

  requestGeolocation(): void {
    if (!navigator.geolocation) {
      this.geoState.set({ status: 'unavailable' });
      return;
    }
    this.geoState.set({ status: 'requesting' });
    navigator.geolocation.getCurrentPosition(
      pos => this.geoState.set({
        status: 'granted',
        lat: pos.coords.latitude,
        lng: pos.coords.longitude,
      }),
      err => this.geoState.set({
        status: err.code === GeolocationPositionError.PERMISSION_DENIED ? 'denied' : 'unavailable'
      }),
      { timeout: 10_000, maximumAge: 5 * 60_000 }
    );
  }

  clearGeolocation(): void {
    this.geoState.set({ status: 'idle' });
  }
}
