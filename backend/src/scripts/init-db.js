// Script para inicializar la base de datos con un usuario admin
import bcrypt from 'bcryptjs';
import db from '../db/database.js';

async function initDb() {
  console.log('🔧 Inicializando base de datos...\n');

  // Crear usuario admin por defecto
  const adminEmail = 'admin@rfep.es';
  const adminPassword = 'Crown2026!'; // Cambiar en producción
  
  const existingAdmin = db.prepare('SELECT id FROM users WHERE email = ?').get(adminEmail);
  
  if (!existingAdmin) {
    const passwordHash = await bcrypt.hash(adminPassword, 12);
    
    db.prepare(
      'INSERT INTO users (email, password_hash, name, role, team_id) VALUES (?, ?, ?, ?, ?)'
    ).run(adminEmail, passwordHash, 'Administrador RFEP', 'admin', 'rfep');
    
    console.log('✅ Usuario admin creado:');
    console.log(`   Email: ${adminEmail}`);
    console.log(`   Password: ${adminPassword}`);
    console.log(`   (¡Cambia esta contraseña en producción!)\n`);
  } else {
    console.log('ℹ️  Usuario admin ya existe\n');
  }

  // Mostrar usuarios existentes
  const users = db.prepare('SELECT id, email, name, role, team_id FROM users').all();
  console.log('📋 Usuarios en la base de datos:');
  users.forEach(u => {
    console.log(`   - ${u.email} (${u.name}) [${u.role}] - Equipo: ${u.team_id}`);
  });

  console.log('\n✨ Base de datos lista!\n');
  process.exit(0);
}

initDb().catch(err => {
  console.error('❌ Error inicializando DB:', err);
  process.exit(1);
});
