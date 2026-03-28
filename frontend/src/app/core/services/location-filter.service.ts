import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class LocationFilterService {
  /** true = US only (default), false = all (international) */
  readonly usOnly = signal(true);

  toggle(): void {
    this.usOnly.update(v => !v);
  }
}
