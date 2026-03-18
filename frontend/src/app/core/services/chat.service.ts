import { inject, Injectable, signal } from '@angular/core';
import { Observable, map, tap, finalize } from 'rxjs';
import { ChatMessage } from '../models/chat.model';
import { ApiService } from './api.service';

@Injectable({ providedIn: 'root' })
export class ChatService {
  private api = inject(ApiService);
  readonly messages = signal<ChatMessage[]>([]);
  readonly loading = signal(false);

  send(text: string): Observable<void> {
    this.messages.update(m => [...m, { role: 'user', content: text }]);
    this.loading.set(true);
    return this.api.post<{ reply: string }>('/chat', { messages: this.messages() }).pipe(
      tap(res => this.messages.update(m => [...m, { role: 'assistant', content: res.reply }])),
      finalize(() => this.loading.set(false)),
      map(() => void 0)
    );
  }

  clear(): void {
    this.messages.set([]);
  }
}
