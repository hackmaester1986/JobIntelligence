import { inject, Injectable, signal } from '@angular/core';
import { Observable, map, tap, finalize } from 'rxjs';
import { ChatMessage } from '../models/chat.model';
import { ApiService } from './api.service';
import { LocationFilterService } from './location-filter.service';

@Injectable({ providedIn: 'root' })
export class ChatService {
  private api = inject(ApiService);
  private locationFilter = inject(LocationFilterService);
  readonly messages = signal<ChatMessage[]>([]);
  readonly loading = signal(false);

  send(text: string): Observable<void> {
    this.messages.update(m => [...m, { role: 'user', content: text }]);
    this.loading.set(true);
    const isUs = this.locationFilter.usOnly() ? true : undefined;
    return this.api.post<{ reply: string }>('/chat', { messages: this.messages(), isUs }).pipe(
      tap(res => this.messages.update(m => [...m, { role: 'assistant', content: res.reply }])),
      finalize(() => this.loading.set(false)),
      map(() => void 0)
    );
  }

  clear(): void {
    this.messages.set([]);
  }
}
