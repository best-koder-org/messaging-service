#!/bin/bash

# Messaging Service Test Script
echo "🚀 Starting Messaging Service Tests..."

# Start the messaging service in the background
cd /home/m/development/DatingApp/messaging-service
echo "📡 Starting messaging service..."
dotnet run --environment Development &
SERVICE_PID=$!

# Wait for service to start
sleep 5

# Test basic API health
echo "🔍 Testing API health..."
curl -s http://localhost:8007/swagger/index.html > /dev/null
if [ $? -eq 0 ]; then
    echo "✅ API is accessible"
else
    echo "❌ API is not accessible"
fi

# Test JWT token requirement
echo "🔐 Testing authentication requirement..."
AUTH_RESULT=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:8007/api/messages/conversations)
if [ "$AUTH_RESULT" = "401" ]; then
    echo "✅ Authentication is properly enforced"
else
    echo "❌ Authentication check failed (got $AUTH_RESULT)"
fi

# Clean up
echo "🧹 Cleaning up..."
kill $SERVICE_PID 2>/dev/null

echo "✨ Messaging service tests completed!"
echo ""
echo "🔥 Key Features Implemented:"
echo "  • Real-time messaging with SignalR"
echo "  • Content moderation (inappropriate language detection)"
echo "  • Spam detection with rate limiting"
echo "  • Personal information protection"
echo "  • User reporting and banning system"
echo "  • JWT authentication"
echo "  • Message persistence with MySQL"
echo "  • RESTful API for message history"
echo ""
echo "🛡️ Safety Features:"
echo "  • Blocks inappropriate content"
echo "  • Prevents personal info sharing (phone, email, address)"
echo "  • Rate limiting to prevent spam"
echo "  • User reporting and automatic banning"
echo "  • IP-based rate limiting"
echo ""
echo "Next: Update YARP gateway to route messaging traffic!"
